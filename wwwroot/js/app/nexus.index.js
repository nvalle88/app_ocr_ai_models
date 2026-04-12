(function () {
    const config = window.nexusIndexConfig || {};

    function q(selector, root = document) {
        return root.querySelector(selector);
    }

    function qa(selector, root = document) {
        return Array.from(root.querySelectorAll(selector));
    }

    function escapeHtml(value) {
        return (value ?? '').toString().replace(/[&<>"']/g, ch => ({
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#39;'
        })[ch]);
    }

    function normalize(value) {
        return (value ?? '')
            .toString()
            .normalize('NFD')
            .replace(/[\u0300-\u036f]/g, '')
            .toLowerCase()
            .trim();
    }

    function formatBytes(bytes) {
        const value = Number(bytes || 0);
        if (value < 1024) return `${value} B`;
        if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`;
        return `${(value / (1024 * 1024)).toFixed(1)} MB`;
    }

    function buildShortCaseCode(caseCode) {
        const raw = (caseCode || '').toString();
        const shortCode = raw.split('-')[0] || raw;
        return `NE-${shortCode.replace(/^NE/i, '')}`;
    }

    function formatCaseDate(value) {
        const date = value instanceof Date ? value : new Date(value);
        return date.toLocaleString('es-EC', {
            day: '2-digit',
            month: '2-digit',
            year: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        });
    }

    function parseJsonSafe(text) {
        try {
            return text ? JSON.parse(text) : {};
        } catch {
            return null;
        }
    }

    async function readResponsePayload(response) {
        const text = await response.text();
        const payload = parseJsonSafe(text);
        if (payload !== null) {
            return payload;
        }

        if (!response.ok) {
            throw new Error(text || response.statusText || 'No se pudo completar la operacion.');
        }

        throw new Error('El servidor devolvio una respuesta inesperada.');
    }

    function getFileExtension(fileName) {
        const parts = (fileName || '').split('.');
        return parts.length > 1 ? `.${parts.pop().toLowerCase()}` : '';
    }

    function getFileKind(fileName) {
        const extension = getFileExtension(fileName);
        if (['.jpg', '.jpeg', '.png', '.webp', '.bmp', '.tif', '.tiff'].includes(extension)) return 'image';
        if (extension === '.pdf') return 'pdf';
        if (['.xml', '.html', '.htm'].includes(extension)) return 'markup';
        return 'file';
    }

    function getFileIcon(fileName) {
        const kind = getFileKind(fileName);
        if (kind === 'image') return 'fa-file-image';
        if (kind === 'pdf') return 'fa-file-pdf';
        if (kind === 'markup') return 'fa-code';
        return 'fa-file-lines';
    }

    function createProgressCardHtml(title, detail, progress, tone = 'default') {
        const toneClass = tone === 'error' ? ' is-error' : '';
        const safeProgress = Math.max(8, Math.min(100, Number(progress || 0)));

        return `
            <div class="row-processing-card${toneClass}">
                <strong>${escapeHtml(title)}</strong>
                <span>${escapeHtml(detail)}</span>
                <div class="row-processing-bar"><span style="width:${safeProgress}%"></span></div>
            </div>`;
    }

    function getStatusHtml(status) {
        if (status === 'evaluado') {
            return `<span class="status-pill is-evaluated"><i class="fa-solid fa-circle-check"></i>Evaluado</span>`;
        }
        if (status === 'processing') {
            return `<span class="status-pill is-processing"><i class="fa-solid fa-spinner fa-spin"></i>Procesando</span>`;
        }
        if (status === 'error') {
            return `<span class="status-pill is-error"><i class="fa-solid fa-triangle-exclamation"></i>Error</span>`;
        }
        return `<span class="status-pill is-pending"><i class="fa-solid fa-hourglass-half"></i>Pendiente</span>`;
    }

    function getTypePresentation(category) {
        const normalized = normalize(category);
        if (normalized.includes('hospital del dia')) return { label: 'Hospital del dia', icon: 'fa-hospital', tone: 'primary' };
        if (normalized.includes('hospitalario')) return { label: 'Hospitalario', icon: 'fa-briefcase-medical', tone: 'info' };
        if (normalized.includes('ambulatorio')) return { label: 'Ambulatorio', icon: 'fa-stethoscope', tone: 'warning' };
        if (normalized.includes('no definido')) return { label: 'No Definido', icon: 'fa-circle-question', tone: 'danger' };
        if (normalized.includes('error')) return { label: 'Error', icon: 'fa-triangle-exclamation', tone: 'danger' };
        return { label: 'No Evaluado', icon: 'fa-wave-square', tone: 'muted' };
    }

    function getTypeHtml(category) {
        const type = getTypePresentation(category);
        return `<span class="type-pill tone-${type.tone}"><i class="fa-solid ${type.icon}"></i>${type.label}</span>`;
    }

    function buildRowSearch(caseLabel, processName, typeLabel, dateText) {
        return `${caseLabel} ${processName} ${typeLabel} ${dateText}`;
    }

    function getActionsHtml(caseCode, caseLabel, includeDetails) {
        const viewButton = `
            <button type="button"
                    class="btn btn-outline-primary btn-sm icon-action btnViewDetails"
                    data-nameproccess="${escapeHtml(caseLabel)}"
                    data-casecode="${escapeHtml(caseCode)}"
                    data-processcode=""
                    title="Ver detalles">
                <i class="fa-solid fa-eye"></i>
            </button>`;

        const processButton = `
            <button type="button"
                    class="btn btn-success btn-sm icon-action btnProcessRow"
                    data-nameproccess="${escapeHtml(caseLabel)}"
                    data-casecode="${escapeHtml(caseCode)}"
                    data-processcode=""
                    title="${includeDetails ? 'Reprocesar' : 'Procesar'}">
                <i class="fa-solid ${includeDetails ? 'fa-rotate-right' : 'fa-play'}"></i>
            </button>`;

        return `<div class="table-actions">${includeDetails ? `${viewButton}${processButton}` : processButton}</div>`;
    }

    function buildCaseRowMarkup(caseData, options = {}) {
        const caseLabel = buildShortCaseCode(caseData.caseCode);
        const processName = caseData.nameProccess || caseData.processName || 'Proceso';
        const startDate = caseData.startDate || new Date();
        const dateText = formatCaseDate(startDate);
        const status = options.status || 'pendiente';
        const typeLabel = options.typeLabel || 'No Evaluado';
        const searchText = buildRowSearch(caseLabel, processName, typeLabel, dateText);

        return `
            <tr class="case-row case-row-enter"
                data-case-code="${escapeHtml(caseData.caseCode)}"
                data-case-label="${escapeHtml(caseLabel)}"
                data-process-name="${escapeHtml(processName)}"
                data-status="${escapeHtml(status)}"
                data-type-label="${escapeHtml(typeLabel)}"
                data-start-iso="${escapeHtml(new Date(startDate).toISOString())}"
                data-search="${escapeHtml(searchText)}">
                <td data-label="Caso">
                    <div class="case-identity">
                        <strong class="case-code">${escapeHtml(caseLabel)}</strong>
                        <span class="case-support">Expediente OCR</span>
                    </div>
                </td>
                <td data-label="Recepcion">
                    <div class="case-date">${escapeHtml(dateText)}</div>
                    <span class="case-support">Ingreso local</span>
                </td>
                <td data-label="Proceso">
                    <span class="table-process-pill">
                        <i class="fa-solid fa-diagram-project"></i>
                        <span>${escapeHtml(processName)}</span>
                    </span>
                </td>
                <td data-label="Estado" data-cell="status">
                    ${getStatusHtml(status)}
                </td>
                <td data-label="Tipo" data-cell="type">
                    ${getTypeHtml(typeLabel)}
                </td>
                <td data-label="Acciones" data-cell="actions">
                    ${getActionsHtml(caseData.caseCode, caseLabel, false)}
                </td>
            </tr>`;
    }

    function buildDraftCaseRowMarkup(context) {
        const createdAt = new Date();
        const dateText = formatCaseDate(createdAt);
        const searchText = buildRowSearch('Creando caso', context.processName, 'Creando', dateText);

        return `
            <tr class="case-row case-row-draft"
                data-transient="true"
                data-case-code=""
                data-case-label="Creando caso"
                data-process-name="${escapeHtml(context.processName)}"
                data-status="processing"
                data-type-label="Creando"
                data-start-iso="${escapeHtml(createdAt.toISOString())}"
                data-search="${escapeHtml(searchText)}">
                <td data-label="Caso">
                    <div class="case-identity">
                        <strong class="case-code case-code-draft">Generando codigo</strong>
                        <span class="case-support">${escapeHtml(`${context.fileCount} documento(s) en preparacion`)}</span>
                    </div>
                </td>
                <td data-label="Recepcion">
                    <div class="case-date">${escapeHtml(dateText)}</div>
                    <span class="case-support">Creacion en progreso</span>
                </td>
                <td data-label="Proceso">
                    <span class="table-process-pill">
                        <i class="fa-solid fa-diagram-project"></i>
                        <span>${escapeHtml(context.processName)}</span>
                    </span>
                </td>
                <td data-label="Estado" data-cell="status">
                    ${getStatusHtml('processing')}
                </td>
                <td data-label="Tipo" data-cell="type">
                    ${createProgressCardHtml('Preparando expediente', 'Armando el paquete documental para Nexus.', 18)}
                </td>
                <td data-label="Acciones" data-cell="actions">
                    <div class="table-actions table-actions-busy">
                        <span class="action-busy-pill">
                            <i class="fa-solid fa-spinner fa-spin"></i>
                            Creando
                        </span>
                    </div>
                </td>
            </tr>`;
    }

    window.handleViewDetails = function (button) {
        const caseCode = button?.dataset?.casecode;
        if (!caseCode) {
            Swal.fire({ icon: 'error', title: 'Error', text: 'No se encontro un codigo de caso valido.' });
            return;
        }

        if (typeof window.cargando === 'function') {
            window.cargando();
        }

        window.location.href = (config.detailsUrlTemplate || '/Nexus/Details1?caseCode=__CASE__').replace('__CASE__', encodeURIComponent(caseCode));
    };

    document.addEventListener('DOMContentLoaded', () => {
        const processInput = q('input[name="ProcessCode"]');
        const processContainer = q('#processContainer');
        const processSelectedIndicator = q('#processSelectedIndicator');
        const processSelectedName = q('#processSelectedName');
        const uploadArea = q('#uploadArea');
        const filesInput = q('#files');
        const previewGallery = q('#previewGallery');
        const fileCountBadge = q('#fileCountBadge');
        const uploadMeta = q('#uploadMeta');
        const btnClearUpload = q('#btnClearUpload');
        const btnCrearCaso = q('#btnCrearCaso');
        const ocrForm = q('#ocrForm');
        const workflowEl = q('#caseCreationWorkflow');
        const workflowTitle = q('#creationStageTitle');
        const workflowDetail = q('#creationStageDetail');
        const workflowBadge = q('#creationStageBadge');
        const workflowLog = q('#creationWorkflowLog');
        const workflowSteps = qa('.workflow-step', workflowEl);
        const casesTableBody = q('#casesTableBody');
        const emptyCasesRow = q('#emptyCasesRow');
        const filterEmptyRow = q('#filterEmptyRow');
        const casesResultsMeta = q('#casesResultsMeta');
        const filterSearch = q('#filterSearch');
        const filterStatus = q('#filterStatus');
        const filterType = q('#filterType');
        const filterProcess = q('#filterProcess');
        const filterPeriod = q('#filterPeriod');
        const btnResetFilters = q('#btnResetFilters');
        const btnResetFiltersInline = q('#btnResetFiltersInline');
        const activeFiltersShell = q('#activeFiltersShell');
        const activeFilterChips = q('#activeFilterChips');
        const metricTotalCases = q('#metricTotalCases');
        const metricEvaluatedCases = q('#metricEvaluatedCases');
        const metricPendingCases = q('#metricPendingCases');
        const metricProcessCount = q('#metricProcessCount');

        const state = {
            hasProcess: false,
            selectedProcessName: '',
            creationInterval: null,
            rowProgressTimers: new Map(),
            draftRow: null
        };

        const creationPhases = [
            { step: 'validate', title: 'Validando informacion de entrada', detail: 'Comprobando proceso, cantidad de archivos y formatos permitidos.', log: 'Entradas listas para iniciar la preparacion del expediente.' },
            { step: 'upload', title: 'Preparando paquete documental', detail: 'Convirtiendo archivos y organizando la carga para el motor OCR.', log: 'Empaquetando documentos para el envio seguro.' },
            { step: 'ocr', title: 'Enviando al OCR', detail: 'Remitiendo los documentos al servicio de lectura inteligente.', log: 'Solicitud enviada al OCR para lectura y deteccion de contenido.' },
            { step: 'extract', title: 'Extrayendo texto y metadatos', detail: 'Recibiendo el contenido procesado para construir el expediente.', log: 'Analizando texto reconocido, paginas y contexto documental.' },
            { step: 'register', title: 'Registrando el caso', detail: 'Guardando expediente, proceso y trazabilidad operativa.', log: 'Persistiendo el caso en Nexus y preparando la fila operativa.' },
            { step: 'ready', title: 'Caso listo para trabajar', detail: 'El expediente ya aparece en la tabla y puede procesarse cuando lo necesites.', log: 'Caso creado correctamente y listo para analisis.' }
        ];

        const rowProcessingPhases = [
            { title: 'Enviando al OCR', detail: 'Preparando lectura del expediente.' },
            { title: 'Extrayendo texto', detail: 'Leyendo contenido y estructura documental.' },
            { title: 'Contrastando reglas', detail: 'Aplicando criterio del proceso seleccionado.' },
            { title: 'Consolidando resultado', detail: 'Publicando categoria y estado final.' }
        ];

        function getSelectedFiles() {
            return Array.from(filesInput?.files || []);
        }

        function updateCreateButtonState() {
            const fileCount = getSelectedFiles().length;
            const canCreate = state.hasProcess && fileCount > 0;

            btnCrearCaso.disabled = !canCreate;
            uploadArea?.classList.toggle('has-files', fileCount > 0);
            uploadArea?.classList.toggle('is-ready', canCreate);
            fileCountBadge.classList.toggle('d-none', fileCount === 0);
            fileCountBadge.textContent = fileCount === 1 ? '1 documento' : `${fileCount} documentos`;

            if (!fileCount) {
                uploadMeta.textContent = 'Agrega archivos para habilitar la creacion del caso';
            } else if (!state.hasProcess) {
                uploadMeta.textContent = `${fileCount} documento(s) listos. Selecciona un proceso para continuar.`;
            } else {
                uploadMeta.textContent = `${fileCount} documento(s) preparados para ${state.selectedProcessName}.`;
            }
        }

        function paintPreviewCardState(card, options = {}) {
            if (!card) return;

            const stateName = options.state || 'staged';
            const badge = q('.preview-card-badge', card);
            const detail = q('.preview-card-detail', card);
            const progress = q('.preview-card-progress span', card);
            const iconClass = options.icon || 'fa-circle-check';

            card.classList.remove('is-staged', 'is-working', 'is-ready', 'is-success', 'is-error');
            card.classList.add(`is-${stateName}`);

            if (badge) {
                badge.innerHTML = `<i class="fa-solid ${iconClass}"></i>${escapeHtml(options.label || 'Listo para preparar')}`;
            }

            if (detail) {
                detail.textContent = options.detail || 'Archivo pendiente de preparacion.';
            }

            if (progress) {
                progress.style.width = `${Math.max(8, Math.min(100, Number(options.progress || 12)))}%`;
            }
        }

        function paintPreviewCardByIndex(index, options) {
            const card = q(`.preview-card[data-file-index="${index}"]`, previewGallery);
            paintPreviewCardState(card, options);
        }

        function paintAllPreviewCards(options) {
            qa('.preview-card', previewGallery).forEach(card => paintPreviewCardState(card, options));
        }

        function renderPreviewGallery() {
            const files = getSelectedFiles();
            previewGallery.innerHTML = '';

            if (!files.length) {
                previewGallery.innerHTML = `
                    <div class="preview-gallery-empty">
                        <i class="fa-solid fa-inbox"></i>
                        <span>Aun no hay archivos listos para procesar.</span>
                    </div>`;
                updateCreateButtonState();
                return;
            }

            files.forEach((file, index) => {
                const item = document.createElement('article');
                item.className = 'preview-card is-staged';
                item.dataset.fileIndex = index.toString();

                const objectUrl = getFileKind(file.name) === 'image' ? URL.createObjectURL(file) : '';

                item.innerHTML = `
                    <div class="preview-card-thumb ${getFileKind(file.name)}">
                        ${objectUrl ? `<img src="${objectUrl}" alt="${escapeHtml(file.name)}" />` : `<i class="fa-solid ${getFileIcon(file.name)}"></i>`}
                    </div>
                    <div class="preview-card-copy">
                        <strong title="${escapeHtml(file.name)}">${escapeHtml(file.name)}</strong>
                        <span class="preview-card-meta">${getFileExtension(file.name).replace('.', '').toUpperCase() || 'FILE'} · ${formatBytes(file.size)}</span>
                        <div class="preview-card-state">
                            <span class="preview-card-badge">
                                <i class="fa-solid fa-sparkles"></i>
                                Listo para preparar
                            </span>
                            <small class="preview-card-detail">Esperando envio del expediente.</small>
                            <div class="preview-card-progress"><span style="width:14%"></span></div>
                        </div>
                    </div>
                    <button type="button" class="preview-card-remove" title="Quitar archivo">
                        <i class="fa-solid fa-xmark"></i>
                    </button>`;

                q('.preview-card-remove', item)?.addEventListener('click', () => {
                    const dt = new DataTransfer();
                    getSelectedFiles().forEach((currentFile, currentIndex) => {
                        if (currentIndex !== index) dt.items.add(currentFile);
                    });
                    filesInput.files = dt.files;
                    filesInput.dispatchEvent(new Event('change'));
                });

                const image = q('img', item);
                if (image) {
                    image.addEventListener('load', () => URL.revokeObjectURL(objectUrl), { once: true });
                    image.addEventListener('error', () => URL.revokeObjectURL(objectUrl), { once: true });
                }

                previewGallery.appendChild(item);
            });

            updateCreateButtonState();
        }

        function ensureDraftRow(context) {
            if (state.draftRow && state.draftRow.isConnected) {
                return state.draftRow;
            }

            emptyCasesRow?.classList.add('d-none');
            casesTableBody.insertAdjacentHTML('afterbegin', buildDraftCaseRowMarkup(context));
            state.draftRow = q('.case-row-draft', casesTableBody);
            return state.draftRow;
        }

        function clearDraftRow() {
            if (state.draftRow && state.draftRow.isConnected) {
                state.draftRow.remove();
            }
            state.draftRow = null;
        }

        function paintDraftRow(title, detail, progress, tone = 'default') {
            const row = state.draftRow;
            if (!row || !row.isConnected) return;

            row.dataset.status = tone === 'error' ? 'error' : 'processing';
            row.dataset.typeLabel = title;
            row.dataset.search = buildRowSearch('Creando caso', row.dataset.processName, title, q('.case-date', row)?.textContent || '');

            const caseSupport = q('.case-support', row);
            const typeCell = q('[data-cell="type"]', row);
            const statusCell = q('[data-cell="status"]', row);

            if (caseSupport) {
                caseSupport.textContent = detail;
            }

            if (statusCell) {
                statusCell.innerHTML = getStatusHtml(tone === 'error' ? 'error' : 'processing');
            }

            if (typeCell) {
                typeCell.innerHTML = createProgressCardHtml(title, detail, progress, tone);
            }
        }

        function resetCreationWorkflow() {
            if (state.creationInterval) {
                clearInterval(state.creationInterval);
                state.creationInterval = null;
            }

            workflowLog.innerHTML = '';
            workflowBadge.textContent = 'En progreso';
            workflowBadge.className = 'workflow-badge';
            workflowTitle.textContent = 'Preparando caso';
            workflowDetail.textContent = 'Validando el proceso y los documentos seleccionados.';
            workflowSteps.forEach(step => step.classList.remove('is-active', 'is-done', 'is-error'));
        }

        function appendWorkflowLog(message, tone = 'neutral') {
            const item = document.createElement('div');
            item.className = `workflow-log-item is-${tone}`;
            item.innerHTML = `
                <span class="workflow-log-dot"></span>
                <div>
                    <strong>${new Date().toLocaleTimeString('es-EC', { hour: '2-digit', minute: '2-digit' })}</strong>
                    <span>${escapeHtml(message)}</span>
                </div>`;
            workflowLog.prepend(item);
        }

        function paintCreationPhase(index, mode = 'progress') {
            const currentPhase = creationPhases[index] || creationPhases[0];
            workflowTitle.textContent = currentPhase.title;
            workflowDetail.textContent = currentPhase.detail;

            const progress = mode === 'success'
                ? 100
                : Math.min(92, 18 + (index * 15));

            if (mode === 'error') {
                paintDraftRow('Creacion interrumpida', currentPhase.detail, progress, 'error');
            } else {
                paintDraftRow(currentPhase.title, currentPhase.detail, progress);
            }

            workflowSteps.forEach((step, stepIndex) => {
                step.classList.toggle('is-active', mode === 'progress' && stepIndex === index);
                step.classList.toggle('is-done', stepIndex < index || mode === 'success');
                step.classList.toggle('is-error', mode === 'error' && stepIndex === index);
            });
        }

        function startCreationWorkflow(context) {
            resetCreationWorkflow();
            ensureDraftRow(context);
            workflowEl.classList.remove('d-none');
            appendWorkflowLog(`Iniciando expediente para ${context.processName} con ${context.fileCount} documento(s).`, 'info');
            paintCreationPhase(0);
            paintAllPreviewCards({
                state: 'working',
                label: 'Preparando lote',
                detail: 'Organizando documentos para su envio.',
                progress: 24,
                icon: 'fa-gear fa-spin'
            });

            let phaseIndex = 0;
            state.creationInterval = setInterval(() => {
                if (phaseIndex >= creationPhases.length - 2) return;
                phaseIndex += 1;
                paintCreationPhase(phaseIndex);
                appendWorkflowLog(creationPhases[phaseIndex].log, 'info');
            }, 1350);

            return {
                async complete(caseData) {
                    if (state.creationInterval) {
                        clearInterval(state.creationInterval);
                        state.creationInterval = null;
                    }

                    paintCreationPhase(creationPhases.length - 1, 'success');
                    workflowBadge.textContent = 'Completado';
                    workflowBadge.className = 'workflow-badge is-success';
                    appendWorkflowLog(`Caso ${buildShortCaseCode(caseData.caseCode)} creado correctamente.`, 'success');
                    paintAllPreviewCards({
                        state: 'success',
                        label: 'Registrado',
                        detail: 'El documento ya forma parte del expediente.',
                        progress: 100,
                        icon: 'fa-circle-check'
                    });
                    await new Promise(resolve => window.setTimeout(resolve, 240));
                },
                fail(message) {
                    if (state.creationInterval) {
                        clearInterval(state.creationInterval);
                        state.creationInterval = null;
                    }

                    const failedIndex = Math.min(creationPhases.length - 2, Math.max(0, workflowSteps.findIndex(step => step.classList.contains('is-active'))));
                    paintCreationPhase(failedIndex, 'error');
                    workflowBadge.textContent = 'Error';
                    workflowBadge.className = 'workflow-badge is-error';
                    workflowTitle.textContent = 'No se pudo crear el caso';
                    workflowDetail.textContent = message || 'Se produjo un error durante la creacion del expediente.';
                    appendWorkflowLog(message || 'La creacion del caso no pudo completarse.', 'error');
                    paintAllPreviewCards({
                        state: 'error',
                        label: 'Error de envio',
                        detail: message || 'No fue posible crear el expediente.',
                        progress: 100,
                        icon: 'fa-triangle-exclamation'
                    });
                    paintDraftRow('Creacion interrumpida', message || 'No fue posible crear el expediente.', 100, 'error');
                }
            };
        }

        function updateMetrics() {
            const rows = qa('.case-row', casesTableBody).filter(row => row.dataset.transient !== 'true');
            const total = rows.length;
            const evaluated = rows.filter(row => row.dataset.status === 'evaluado').length;
            const pending = rows.filter(row => row.dataset.status !== 'evaluado').length;
            const processCount = new Set(rows.map(row => row.dataset.processName).filter(Boolean)).size;

            metricTotalCases.textContent = total;
            metricEvaluatedCases.textContent = evaluated;
            metricPendingCases.textContent = pending;
            metricProcessCount.textContent = processCount;
        }

        function rebuildProcessFilterOptions() {
            const currentValue = filterProcess.value;
            const processNames = qa('.case-row', casesTableBody)
                .filter(row => row.dataset.transient !== 'true')
                .map(row => row.dataset.processName)
                .filter(Boolean)
                .sort((left, right) => left.localeCompare(right));

            const uniqueProcessNames = [...new Set(processNames)];
            filterProcess.innerHTML = '<option value="">Todos los procesos</option>' +
                uniqueProcessNames.map(name => `<option value="${escapeHtml(name)}">${escapeHtml(name)}</option>`).join('');

            if (uniqueProcessNames.includes(currentValue)) {
                filterProcess.value = currentValue;
            }
        }

        function clearAllFilters() {
            filterSearch.value = '';
            filterStatus.value = '';
            filterType.value = '';
            filterProcess.value = '';
            if (filterPeriod) {
                filterPeriod.value = '';
            }
            syncCasesTable();
        }

        function renderActiveFilters() {
            if (!activeFiltersShell || !activeFilterChips) return;

            const activeItems = [];
            if (filterSearch.value.trim()) activeItems.push({ key: 'search', label: `Busqueda: ${filterSearch.value.trim()}` });
            if (filterStatus.value) activeItems.push({ key: 'status', label: `Estado: ${filterStatus.options[filterStatus.selectedIndex]?.text || filterStatus.value}` });
            if (filterType.value) activeItems.push({ key: 'type', label: `Tipo: ${filterType.options[filterType.selectedIndex]?.text || filterType.value}` });
            if (filterProcess.value) activeItems.push({ key: 'process', label: `Proceso: ${filterProcess.options[filterProcess.selectedIndex]?.text || filterProcess.value}` });
            if (filterPeriod?.value) activeItems.push({ key: 'period', label: `Periodo: ${filterPeriod.options[filterPeriod.selectedIndex]?.text || filterPeriod.value}` });

            activeFiltersShell.classList.toggle('d-none', activeItems.length === 0);
            activeFilterChips.innerHTML = activeItems.map(item => `
                <button type="button" class="active-filter-chip" data-filter-key="${item.key}">
                    <span>${escapeHtml(item.label)}</span>
                    <i class="fa-solid fa-xmark"></i>
                </button>`).join('');
        }

        function syncCasesTable() {
            const rows = qa('.case-row', casesTableBody);
            const totalRows = rows.filter(row => row.dataset.transient !== 'true').length;
            const term = normalize(filterSearch.value);
            const statusValue = normalize(filterStatus.value);
            const typeValue = normalize(filterType.value);
            const processValue = normalize(filterProcess.value);
            const periodValue = normalize(filterPeriod?.value);
            const now = Date.now();

            let visible = 0;
            rows.forEach(row => {
                const isTransient = row.dataset.transient === 'true';
                const matchesSearch = !term || normalize(row.dataset.search).includes(term);
                const matchesStatus = !statusValue || normalize(row.dataset.status) === statusValue;
                const matchesType = !typeValue || normalize(row.dataset.typeLabel) === typeValue;
                const matchesProcess = !processValue || normalize(row.dataset.processName) === processValue;
                let matchesPeriod = true;

                if (periodValue && !isTransient) {
                    const startIso = row.dataset.startIso;
                    const rowTime = startIso ? new Date(startIso).getTime() : Number.NaN;

                    if (!Number.isNaN(rowTime)) {
                        if (periodValue === 'today') {
                            const today = new Date();
                            today.setHours(0, 0, 0, 0);
                            matchesPeriod = rowTime >= today.getTime();
                        } else if (periodValue === '48h') {
                            matchesPeriod = rowTime >= (now - (48 * 60 * 60 * 1000));
                        } else if (periodValue === '7d') {
                            matchesPeriod = rowTime >= (now - (7 * 24 * 60 * 60 * 1000));
                        }
                    }
                }

                const shouldShow = isTransient || (matchesSearch && matchesStatus && matchesType && matchesProcess && matchesPeriod);

                row.classList.toggle('d-none', !shouldShow);
                if (shouldShow && !isTransient) visible += 1;
            });

            emptyCasesRow?.classList.toggle('d-none', totalRows > 0);
            filterEmptyRow?.classList.toggle('d-none', !(totalRows > 0 && visible === 0));

            if (totalRows === 0) {
                casesResultsMeta.textContent = 'Aun no hay casos cargados';
            } else {
                casesResultsMeta.textContent = `${visible} de ${totalRows} casos visibles`;
            }

            renderActiveFilters();
            updateMetrics();
        }

        function clearUploadSelection() {
            filesInput.value = '';
            renderPreviewGallery();
        }

        function activateProcessButton(targetButton, process) {
            qa('.process-chip', processContainer).forEach(button => {
                button.classList.remove('is-active');
            });

            targetButton.classList.add('is-active');
            processInput.value = process.processId;
            state.hasProcess = true;
            state.selectedProcessName = process.processName;
            processSelectedName.textContent = process.processName;
            processSelectedIndicator.classList.remove('d-none');
            updateCreateButtonState();
        }

        async function loadProcessButtons() {
            try {
                const response = await fetch(config.processesUrl || '/Nexus/GetProcessesByUser');
                if (!response.ok) throw new Error('No se pudieron cargar los procesos.');
                const payload = await response.json();
                const processes = payload.processes || [];

                processContainer.innerHTML = '';

                if (!processes.length) {
                    processContainer.innerHTML = `
                        <div class="process-loading-card is-empty">
                            <i class="fa-solid fa-triangle-exclamation"></i>
                            <span>No hay procesos disponibles para este usuario.</span>
                        </div>`;
                    return;
                }

                processes.forEach(process => {
                    const button = document.createElement('button');
                    button.type = 'button';
                    button.className = 'process-chip';
                    button.innerHTML = `
                        <span class="process-chip-icon"><i class="fa-solid fa-layer-group"></i></span>
                        <span class="process-chip-copy">
                            <strong>${escapeHtml(process.processName)}</strong>
                            <small>${escapeHtml(process.description || 'Proceso listo para carga documental')}</small>
                        </span>`;

                    button.addEventListener('click', () => activateProcessButton(button, process));
                    processContainer.appendChild(button);
                });
            } catch (error) {
                console.error(error);
                processContainer.innerHTML = `
                    <div class="process-loading-card is-empty">
                        <i class="fa-solid fa-triangle-exclamation"></i>
                        <span>No se pudieron cargar los procesos autorizados.</span>
                    </div>`;
                Swal.fire({ icon: 'error', title: 'Error', text: 'No se pudieron cargar los procesos.' });
            }
        }

        function makeCaseFilePayload(file) {
            return new Promise(resolve => {
                const reader = new FileReader();
                reader.onload = () => resolve({
                    FileName: file.name,
                    Content: reader.result.split(',')[1],
                    Extension: getFileExtension(file.name)
                });
                reader.readAsDataURL(file);
            });
        }

        async function convertFilesForSubmission(files) {
            const payloads = [];

            for (let index = 0; index < files.length; index += 1) {
                const file = files[index];
                paintPreviewCardByIndex(index, {
                    state: 'working',
                    label: 'Preparando archivo',
                    detail: 'Convirtiendo el documento para el envio seguro.',
                    progress: 48,
                    icon: 'fa-gear fa-spin'
                });

                const payload = await makeCaseFilePayload(file);
                payloads.push(payload);

                paintPreviewCardByIndex(index, {
                    state: 'ready',
                    label: 'Documento listo',
                    detail: 'El archivo ya puede entrar al flujo OCR.',
                    progress: 100,
                    icon: 'fa-circle-check'
                });
            }

            return payloads;
        }

        function stopRowProcessing(row) {
            const caseCode = row?.dataset.caseCode;
            if (!caseCode) return;

            const timer = state.rowProgressTimers.get(caseCode);
            if (timer) {
                clearInterval(timer);
                state.rowProgressTimers.delete(caseCode);
            }
        }

        function paintRowProcessing(row, phaseIndex = 0) {
            const currentPhase = rowProcessingPhases[phaseIndex] || rowProcessingPhases[0];
            const statusCell = q('[data-cell="status"]', row);
            const typeCell = q('[data-cell="type"]', row);
            const actionsCell = q('[data-cell="actions"]', row);
            const progress = Math.min(92, 26 + (phaseIndex * 22));

            row.dataset.status = 'processing';
            row.dataset.typeLabel = currentPhase.title;
            row.dataset.search = buildRowSearch(row.dataset.caseLabel, row.dataset.processName, currentPhase.title, q('.case-date', row)?.textContent || '');
            row.classList.add('is-processing');

            if (statusCell) {
                statusCell.innerHTML = getStatusHtml('processing');
            }

            if (typeCell) {
                typeCell.innerHTML = createProgressCardHtml(currentPhase.title, currentPhase.detail, progress);
            }

            if (actionsCell) {
                actionsCell.innerHTML = `
                    <div class="table-actions table-actions-busy">
                        <span class="action-busy-pill">
                            <i class="fa-solid fa-spinner fa-spin"></i>
                            Trabajando
                        </span>
                    </div>`;
            }
        }

        async function processCase(button) {
            if (!button || button.disabled) return;

            const row = button.closest('tr');
            if (!row) return;

            const caseCode = button.dataset.casecode;
            const caseLabel = row.dataset.caseLabel || button.dataset.nameproccess || buildShortCaseCode(caseCode);
            stopRowProcessing(row);

            let phaseIndex = 0;
            paintRowProcessing(row, phaseIndex);
            const timer = setInterval(() => {
                phaseIndex = (phaseIndex + 1) % rowProcessingPhases.length;
                paintRowProcessing(row, phaseIndex);
            }, 1500);
            state.rowProgressTimers.set(caseCode, timer);

            try {
                const form = new FormData();
                form.append('caseCode', caseCode);

                const response = await fetch(config.processCaseUrl || '/Nexus/ProcessCaseAjax', {
                    method: 'POST',
                    body: form
                });

                const payload = await readResponsePayload(response);
                if (!response.ok) throw new Error(payload.message || 'No se pudo procesar el caso.');

                stopRowProcessing(row);
                row.classList.remove('is-processing');
                row.classList.add('case-row-highlight');
                row.dataset.status = 'evaluado';
                row.dataset.typeLabel = payload.typeCase || 'No Definido';
                row.dataset.search = buildRowSearch(caseLabel, row.dataset.processName, row.dataset.typeLabel, q('.case-date', row)?.textContent || '');

                q('[data-cell="status"]', row).innerHTML = getStatusHtml('evaluado');
                q('[data-cell="type"]', row).innerHTML = getTypeHtml(payload.typeCase || 'No Definido');
                q('[data-cell="actions"]', row).innerHTML = getActionsHtml(caseCode, caseLabel, true);

                window.setTimeout(() => row.classList.remove('case-row-highlight'), 2200);
                syncCasesTable();
            } catch (error) {
                console.error(error);
                stopRowProcessing(row);
                row.classList.remove('is-processing');
                row.dataset.status = 'error';
                row.dataset.typeLabel = 'Error';
                row.dataset.search = buildRowSearch(caseLabel, row.dataset.processName, 'Error', q('.case-date', row)?.textContent || '');

                q('[data-cell="status"]', row).innerHTML = getStatusHtml('error');
                q('[data-cell="type"]', row).innerHTML = createProgressCardHtml('Error operativo', 'No fue posible completar el procesamiento.', 100, 'error');
                q('[data-cell="actions"]', row).innerHTML = getActionsHtml(caseCode, caseLabel, false);
                syncCasesTable();

                Swal.fire({
                    icon: 'warning',
                    title: 'Advertencia',
                    text: error.message || 'No se pudo procesar el caso.'
                });
            }
        }

        uploadArea?.addEventListener('click', () => filesInput.click());
        btnClearUpload?.addEventListener('click', clearUploadSelection);

        uploadArea?.addEventListener('dragover', event => {
            event.preventDefault();
            uploadArea.classList.add('is-dragover');
        });

        uploadArea?.addEventListener('dragleave', event => {
            event.preventDefault();
            uploadArea.classList.remove('is-dragover');
        });

        uploadArea?.addEventListener('drop', event => {
            event.preventDefault();
            uploadArea.classList.remove('is-dragover');
            const dt = new DataTransfer();
            Array.from(event.dataTransfer.files || []).forEach(file => dt.items.add(file));
            filesInput.files = dt.files;
            filesInput.dispatchEvent(new Event('change'));
        });

        filesInput?.addEventListener('change', renderPreviewGallery);

        ocrForm?.addEventListener('submit', async event => {
            event.preventDefault();

            if (!state.hasProcess || !getSelectedFiles().length) {
                Swal.fire({
                    icon: 'warning',
                    title: 'Advertencia',
                    text: 'Selecciona un proceso y al menos un documento antes de crear el caso.'
                });
                return;
            }

            btnCrearCaso.disabled = true;
            const files = getSelectedFiles();
            const workflow = startCreationWorkflow({
                fileCount: files.length,
                processName: state.selectedProcessName || 'Proceso seleccionado'
            });

            try {
                appendWorkflowLog('Convirtiendo documentos para el envio.', 'info');
                const payloadFiles = await convertFilesForSubmission(files);
                paintAllPreviewCards({
                    state: 'working',
                    label: 'Enviando al flujo OCR',
                    detail: 'Los documentos estan viajando al motor de extraccion.',
                    progress: 72,
                    icon: 'fa-cloud-arrow-up'
                });

                const response = await fetch(config.createCaseUrl || '/Nexus/CreateCaseProcess', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        ProcessCode: processInput.value,
                        Query: '',
                        Files: payloadFiles
                    })
                });

                const payload = await readResponsePayload(response);
                if (!response.ok) throw new Error(payload.message || response.statusText || 'No se pudo crear el caso.');

                await workflow.complete(payload);

                const liveMarkup = buildCaseRowMarkup(payload, {
                    status: 'pendiente',
                    typeLabel: 'No Evaluado'
                });

                let insertedRow = null;
                if (state.draftRow && state.draftRow.isConnected) {
                    state.draftRow.insertAdjacentHTML('beforebegin', liveMarkup);
                    insertedRow = state.draftRow.previousElementSibling;
                    clearDraftRow();
                } else {
                    emptyCasesRow?.classList.add('d-none');
                    casesTableBody.insertAdjacentHTML('afterbegin', liveMarkup);
                    insertedRow = q('.case-row', casesTableBody);
                }

                insertedRow?.classList.add('case-row-highlight');
                window.setTimeout(() => insertedRow?.classList.remove('case-row-highlight'), 2200);

                clearUploadSelection();
                syncCasesTable();
                rebuildProcessFilterOptions();
            } catch (error) {
                console.error(error);
                workflow.fail(error.message || 'No se pudo crear el caso.');
                if (state.draftRow && state.draftRow.isConnected) {
                    window.setTimeout(() => {
                        clearDraftRow();
                        syncCasesTable();
                    }, 4200);
                }
                Swal.fire({
                    icon: 'warning',
                    title: 'Advertencia',
                    text: 'Error al crear el caso: ' + (error.message || 'Error en la solicitud')
                });
            } finally {
                updateCreateButtonState();
            }
        });

        [filterSearch, filterStatus, filterType, filterProcess].forEach(control => {
            control?.addEventListener('input', syncCasesTable);
            control?.addEventListener('change', syncCasesTable);
        });

        filterPeriod?.addEventListener('input', syncCasesTable);
        filterPeriod?.addEventListener('change', syncCasesTable);

        btnResetFilters?.addEventListener('click', clearAllFilters);
        btnResetFiltersInline?.addEventListener('click', clearAllFilters);

        activeFilterChips?.addEventListener('click', event => {
            const chip = event.target.closest('.active-filter-chip');
            if (!chip) return;

            const key = chip.dataset.filterKey;
            if (key === 'search') filterSearch.value = '';
            if (key === 'status') filterStatus.value = '';
            if (key === 'type') filterType.value = '';
            if (key === 'process') filterProcess.value = '';
            if (key === 'period' && filterPeriod) filterPeriod.value = '';
            syncCasesTable();
        });

        document.addEventListener('click', event => {
            const processButton = event.target.closest('.btnProcessRow');
            if (processButton) {
                processCase(processButton);
            }

            const detailsButton = event.target.closest('.btnViewDetails');
            if (detailsButton) {
                window.handleViewDetails(detailsButton);
            }
        });

        loadProcessButtons();
        renderPreviewGallery();
        syncCasesTable();
    });
})();
