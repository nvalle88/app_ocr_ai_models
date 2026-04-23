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

    function buildDetailsUrl(caseCode) {
        return (config.detailsUrlTemplate || '/Nexus/Details1?caseCode=__CASE__')
            .replace('__CASE__', encodeURIComponent(caseCode || ''));
    }

    function buildCaseConversationPreviewUrl(caseCode) {
        return (config.caseConversationPreviewUrlTemplate || '/Nexus/ConversationPreview?caseCode=__CASE__')
            .replace('__CASE__', encodeURIComponent(caseCode || ''));
    }

    async function fetchCaseConversationPreviewHtml(caseCode, signal) {
        const response = await fetch(buildCaseConversationPreviewUrl(caseCode), {
            method: 'GET',
            headers: {
                'Accept': 'text/html',
                'X-Requested-With': 'XMLHttpRequest'
            },
            credentials: 'same-origin',
            signal
        });

        if (!response.ok) {
            throw new Error(`No se pudo cargar la conversacion (${response.status}).`);
        }

        return response.text();
    }

    function buildCaseDocumentsUrl(caseCode) {
        return (config.caseDocumentsUrlTemplate || '/Nexus/GetCaseDocuments?caseCode=__CASE__')
            .replace('__CASE__', encodeURIComponent(caseCode || ''));
    }

    function buildCaseDocumentsZipUrl(caseCode) {
        return (config.caseDocumentsZipUrlTemplate || '/Nexus/DownloadCaseDocumentsZip?caseCode=__CASE__')
            .replace('__CASE__', encodeURIComponent(caseCode || ''));
    }

    function buildCaseDocumentDownloadUrl(caseCode, fileId) {
        return (config.caseDocumentDownloadUrlTemplate || '/Nexus/DownloadCaseDocument?caseCode=__CASE__&fileId=__FILE__')
            .replace('__CASE__', encodeURIComponent(caseCode || ''))
            .replace('__FILE__', encodeURIComponent(fileId || ''));
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

    function parseDownloadFileName(contentDisposition, fallbackName) {
        const rawHeader = (contentDisposition || '').toString();
        const utf8Match = rawHeader.match(/filename\*=UTF-8''([^;]+)/i);
        if (utf8Match?.[1]) {
            return decodeURIComponent(utf8Match[1]).replace(/["]/g, '').trim() || fallbackName;
        }

        const plainMatch = rawHeader.match(/filename="?([^\";]+)"?/i);
        if (plainMatch?.[1]) {
            return plainMatch[1].trim() || fallbackName;
        }

        return fallbackName;
    }

    function triggerBlobDownload(blob, fileName) {
        const downloadUrl = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = downloadUrl;
        anchor.download = fileName;
        anchor.style.display = 'none';
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();

        window.setTimeout(() => {
            try { URL.revokeObjectURL(downloadUrl); } catch (error) { /* ignore */ }
        }, 1500);
    }

    async function downloadCaseDocumentsZip(caseCode, caseLabel, triggerButton = null) {
        if (!caseCode) {
            throw new Error('No se encontro el caso para descargar el ZIP.');
        }

        const fallbackName = `NE-${buildShortCaseCode(caseLabel || caseCode)}-documentos.zip`;
        if (triggerButton) {
            triggerButton.disabled = true;
            triggerButton.setAttribute('aria-busy', 'true');
        }

        try {
            const response = await fetch(buildCaseDocumentsZipUrl(caseCode), {
                method: 'GET',
                headers: { 'Accept': 'application/zip, application/octet-stream' }
            });

            if (!response.ok) {
                const payload = await readResponsePayload(response);
                throw new Error(payload?.message || payload?.error || response.statusText || 'No se pudo descargar el ZIP del caso.');
            }

            const blob = await response.blob();
            const fileName = parseDownloadFileName(response.headers.get('content-disposition'), fallbackName);
            triggerBlobDownload(blob, fileName);
        } finally {
            if (triggerButton) {
                triggerButton.disabled = false;
                triggerButton.setAttribute('aria-busy', 'false');
            }
        }
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
        const chatButton = `
            <button type="button"
                    class="btn btn-outline-dark btn-sm icon-action btnShowCaseChat"
                    data-casecode="${escapeHtml(caseCode)}"
                    data-caselabel="${escapeHtml(caseLabel)}"
                    title="Ver chat completo">
                <i class="fa-solid fa-comments"></i>
            </button>`;

        const viewButton = `
            <button type="button"
                    class="btn btn-outline-primary btn-sm icon-action btnViewDetails"
                    data-nameproccess="${escapeHtml(caseLabel)}"
                    data-casecode="${escapeHtml(caseCode)}"
                    data-processcode=""
                    title="Ver detalles">
                <i class="fa-solid fa-eye"></i>
            </button>`;

        const thumbsButton = `
            <button type="button"
                    class="btn btn-outline-info btn-sm icon-action btnShowCaseDocuments"
                    data-casecode="${escapeHtml(caseCode)}"
                    data-caselabel="${escapeHtml(caseLabel)}"
                    title="Ver miniaturas">
                <i class="fa-solid fa-images"></i>
            </button>`;

        const zipButton = `
            <button type="button"
               class="btn btn-outline-secondary btn-sm icon-action btnDownloadCaseZip"
               data-casecode="${escapeHtml(caseCode)}"
               data-caselabel="${escapeHtml(caseLabel)}"
               title="Descargar documentos ZIP">
                <i class="fa-solid fa-file-zipper"></i>
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

        return `<div class="table-actions">${includeDetails ? `${chatButton}${viewButton}${thumbsButton}${zipButton}${processButton}` : `${chatButton}${thumbsButton}${zipButton}${processButton}`}</div>`;
    }

    function buildCaseDocumentCardHtml(file, caseCode) {
        const originalName = file?.originalName || file?.url || `Documento ${file?.id || ''}`;
        const type = normalize(file?.type || getFileKind(originalName));
        const isImage = type === 'img' || type === 'image';
        const createdDate = file?.createdDate ? formatCaseDate(file.createdDate) : '';
        const thumbHtml = isImage && file?.url
            ? `<img src="${escapeHtml(file.url)}" alt="${escapeHtml(originalName)}" loading="lazy" />`
            : (() => {
                const iconClass = type === 'pdf'
                    ? 'fa-file-pdf'
                    : type === 'markup'
                        ? 'fa-file-code'
                        : isImage
                            ? 'fa-file-image'
                            : 'fa-file-lines';
                return `<i class="fa-solid ${iconClass}" aria-hidden="true"></i>`;
            })();

        const typeLabel = isImage
            ? 'Imagen'
            : type === 'pdf'
                ? 'PDF'
                : type === 'markup'
                    ? 'XML / HTML'
                    : 'Archivo';

        return `
            <article class="case-documents-card">
                <div class="case-documents-card-thumb is-${escapeHtml(isImage ? 'image' : type)}">
                    ${thumbHtml}
                </div>
                <div class="case-documents-card-copy">
                    <strong title="${escapeHtml(originalName)}">${escapeHtml(originalName)}</strong>
                    <small>${escapeHtml(typeLabel)}${createdDate ? ` • ${escapeHtml(createdDate)}` : ''}</small>
                </div>
                <div class="case-documents-card-actions">
                    ${file?.url ? `
                        <a href="${escapeHtml(file.url)}"
                           class="btn btn-light btn-sm"
                           target="_blank"
                           rel="noopener noreferrer">
                            Abrir
                        </a>` : ''}
                    <a href="${escapeHtml(buildCaseDocumentDownloadUrl(caseCode, file?.id))}"
                       class="btn btn-outline-secondary btn-sm">
                        Descargar
                    </a>
                </div>
            </article>`;
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

        window.location.href = buildDetailsUrl(caseCode);
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
        const workflowModal = q('#creationWorkflowModal');
        const workflowEl = q('#caseCreationWorkflow');
        const workflowTitle = q('#creationStageTitle');
        const workflowDetail = q('#creationStageDetail');
        const workflowBadge = q('#creationStageBadge');
        const workflowLog = q('#creationWorkflowLog');
        const btnCloseWorkflowModal = q('#btnCloseWorkflowModal');
        const workflowCardTitle = q('#workflowCardTitle');
        const workflowCardDetail = q('#workflowCardDetail');
        const workflowCardBar = q('#workflowCardBar');
        const workflowProgressCard = q('#workflowProgressCard');
        const creationSuccessActions = q('#creationSuccessActions');
        const creationSuccessTitle = q('#creationSuccessTitle');
        const creationSuccessDetail = q('#creationSuccessDetail');
        const btnGoCreatedCase = q('#btnGoCreatedCase');
        const btnStayOnIndex = q('#btnStayOnIndex');
        const aiAnalysisPanel = q('#aiAnalysisPanel');
        const aiCardTitle = q('#aiCardTitle');
        const aiCardDetail = q('#aiCardDetail');
        const aiCardBar = q('#aiCardBar');
        const aiProgressCard = q('#aiProgressCard');
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
        const pageSizeSelect = q('#pageSizeSelect');
        const paginationMetaTitle = q('.table-pagination-meta strong');
        const paginationMetaDetail = q('.table-pagination-meta span');
        const tableShell = q('.table-shell');
        const casesSection = q('#casesSection');
        const caseDocumentsModal = q('#caseDocumentsModal');
        const caseDocumentsModalTitle = q('#caseDocumentsModalTitle');
        const caseDocumentsModalSubtitle = q('#caseDocumentsModalSubtitle');
        const caseDocumentsModalState = q('#caseDocumentsModalState');
        const caseDocumentsModalGrid = q('#caseDocumentsModalGrid');
        const btnCloseCaseDocumentsModal = q('#btnCloseCaseDocumentsModal');
        const btnCaseDocumentsZipModal = q('#btnCaseDocumentsZipModal');
        const caseChatModal = q('#caseChatModal');
        const caseChatModalTitle = q('#caseChatModalTitle');
        const caseChatModalSubtitle = q('#caseChatModalSubtitle');
        const caseChatModalState = q('#caseChatModalState');
        const caseChatModalFrame = q('#caseChatModalFrame');
        const btnCloseCaseChatModal = q('#btnCloseCaseChatModal');
        const btnCaseChatOpenDetails = q('#btnCaseChatOpenDetails');

        const state = {
            hasProcess: false,
            selectedProcessName: '',
            creationInterval: null,
            rowProgressTimers: new Map(),
            draftRow: null,
            selectedFiles: [],
            lastCreatedCase: null,
            isOpeningCreatedCase: false,
            filterTimer: null,
            metrics: {
                total: Number((config.totalCases ?? metricTotalCases?.textContent) || 0),
                evaluated: Number((config.evaluatedCases ?? metricEvaluatedCases?.textContent) || 0),
                pending: Number((config.pendingCases ?? metricPendingCases?.textContent) || 0),
                processCount: Number((config.processCount ?? metricProcessCount?.textContent) || 0)
            }
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

        /* ── Claude-like smooth text transition helper ── */
        let activeCaseDocumentsCode = '';
        let activeCaseConversationCode = '';
        let caseConversationRequestController = null;

        function setCaseDocumentsModal(open) {
            if (!caseDocumentsModal) return;

            const shouldOpen = !!open;
            caseDocumentsModal.classList.toggle('d-none', !shouldOpen);
            caseDocumentsModal.setAttribute('aria-hidden', shouldOpen ? 'false' : 'true');
            document.body.classList.toggle('case-documents-modal-open', shouldOpen);
        }

        function renderCaseDocumentsState(iconClass, message, isError = false) {
            if (!caseDocumentsModalState || !caseDocumentsModalGrid) return;

            caseDocumentsModalGrid.classList.add('d-none');
            caseDocumentsModalGrid.innerHTML = '';
            caseDocumentsModalState.classList.remove('d-none', 'is-error');
            caseDocumentsModalState.classList.toggle('is-error', !!isError);
            caseDocumentsModalState.innerHTML = `
                <i class="fa-solid ${iconClass}" aria-hidden="true"></i>
                <span>${escapeHtml(message)}</span>`;
        }

        function renderCaseDocumentsGrid(items, caseCode, caseLabel) {
            if (!caseDocumentsModalState || !caseDocumentsModalGrid) return;

            if (!Array.isArray(items) || !items.length) {
                caseDocumentsModalTitle.textContent = `Miniaturas de ${caseLabel}`;
                caseDocumentsModalSubtitle.textContent = 'Este caso no tiene documentos disponibles.';
                renderCaseDocumentsState('fa-folder-open', 'Este caso no tiene documentos para mostrar.');
                return;
            }

            caseDocumentsModalTitle.textContent = `Miniaturas de ${caseLabel}`;
            caseDocumentsModalSubtitle.textContent = `${items.length} documento(s) disponibles para consulta rápida.`;
            caseDocumentsModalState.classList.add('d-none');
            caseDocumentsModalGrid.classList.remove('d-none');
            caseDocumentsModalGrid.innerHTML = items.map(item => buildCaseDocumentCardHtml(item, caseCode)).join('');
        }

        async function openCaseDocumentsModal(caseCode, caseLabel) {
            if (!caseCode || !caseDocumentsModal) return;

            activeCaseDocumentsCode = caseCode;
            setCaseDocumentsModal(true);
            caseDocumentsModalTitle.textContent = `Miniaturas de ${caseLabel}`;
            caseDocumentsModalSubtitle.textContent = 'Cargando documentos del expediente...';
            renderCaseDocumentsState('fa-spinner fa-spin', 'Cargando documentos del caso...');

            if (btnCaseDocumentsZipModal) {
                btnCaseDocumentsZipModal.dataset.casecode = caseCode;
                btnCaseDocumentsZipModal.dataset.caselabel = caseLabel;
                btnCaseDocumentsZipModal.classList.remove('d-none');
            }

            try {
                const response = await fetch(buildCaseDocumentsUrl(caseCode), {
                    method: 'GET',
                    headers: { 'Accept': 'application/json' }
                });

                if (!response.ok) {
                    const payload = await readResponsePayload(response);
                    throw new Error(payload?.message || payload?.error || response.statusText || 'No se pudieron consultar los documentos.');
                }

                const payload = await response.json();
                renderCaseDocumentsGrid(payload, caseCode, caseLabel);
            } catch (error) {
                console.error(error);
                caseDocumentsModalTitle.textContent = `Miniaturas de ${caseLabel}`;
                caseDocumentsModalSubtitle.textContent = 'No fue posible cargar los documentos del caso.';
                renderCaseDocumentsState('fa-triangle-exclamation', error?.message || 'No se pudieron cargar los documentos.', true);
            }
        }

        function closeCaseDocumentsModal() {
            if (!caseDocumentsModal) return;

            activeCaseDocumentsCode = '';
            setCaseDocumentsModal(false);
            caseDocumentsModalGrid?.classList.add('d-none');
            if (caseDocumentsModalGrid) caseDocumentsModalGrid.innerHTML = '';
            caseDocumentsModalState?.classList.remove('is-error');
            if (btnCaseDocumentsZipModal) {
                btnCaseDocumentsZipModal.classList.add('d-none');
                btnCaseDocumentsZipModal.dataset.casecode = '';
                btnCaseDocumentsZipModal.dataset.caselabel = '';
            }
        }

        function setCaseChatModal(open) {
            if (!caseChatModal) return;

            const shouldOpen = !!open;
            caseChatModal.classList.toggle('d-none', !shouldOpen);
            caseChatModal.setAttribute('aria-hidden', shouldOpen ? 'false' : 'true');
            document.body.classList.toggle('case-chat-modal-open', shouldOpen);
        }

        function renderCaseChatState(iconClass, message, isError = false) {
            if (!caseChatModalState) return;

            caseChatModalFrame?.classList.add('d-none');
            caseChatModalState.classList.remove('d-none', 'is-error');
            caseChatModalState.classList.toggle('is-error', !!isError);
            caseChatModalState.innerHTML = `
                <i class="fa-solid ${iconClass}" aria-hidden="true"></i>
                <span>${escapeHtml(message)}</span>`;
        }

        async function openCaseChatModal(caseCode, caseLabel) {
            if (!caseCode || !caseChatModal || !caseChatModalFrame) return;

            activeCaseConversationCode = caseCode;
            setCaseChatModal(true);
            caseChatModalTitle.textContent = `Chat completo de ${caseLabel}`;
            caseChatModalSubtitle.textContent = 'Cargando conversación del expediente...';
            renderCaseChatState('fa-spinner fa-spin', 'Preparando el historial del caso...');

            if (btnCaseChatOpenDetails) {
                btnCaseChatOpenDetails.href = buildDetailsUrl(caseCode);
            }

            if (caseConversationRequestController) {
                caseConversationRequestController.abort();
            }

            const requestController = new AbortController();
            caseConversationRequestController = requestController;
            caseChatModalFrame.removeAttribute('src');
            caseChatModalFrame.removeAttribute('srcdoc');
            caseChatModalFrame.classList.add('d-none');
            caseChatModalFrame.onload = () => {
                if (activeCaseConversationCode !== caseCode || caseConversationRequestController !== requestController) return;
                caseChatModalSubtitle.textContent = 'Vista rápida de la conversación sin salir del índice.';
                caseChatModalState.classList.add('d-none');
                caseChatModalFrame.classList.remove('d-none');
            };

            try {
                const previewHtml = await fetchCaseConversationPreviewHtml(caseCode, requestController.signal);
                if (activeCaseConversationCode !== caseCode || caseConversationRequestController !== requestController) return;
                caseChatModalFrame.srcdoc = previewHtml;
                window.setTimeout(() => {
                    if (activeCaseConversationCode !== caseCode || caseConversationRequestController !== requestController) return;
                    if (!caseChatModalFrame.classList.contains('d-none')) return;
                    caseChatModalState.classList.add('d-none');
                    caseChatModalFrame.classList.remove('d-none');
                }, 180);
            } catch (error) {
                if (error && error.name === 'AbortError') return;

                console.error('Case chat preview error', error);
                if (activeCaseConversationCode !== caseCode || caseConversationRequestController !== requestController) return;

                caseChatModalSubtitle.textContent = 'No fue posible cargar la conversación del expediente.';
                renderCaseChatState('fa-circle-exclamation', (error && error.message) ? error.message : 'No se pudo abrir la vista rápida del chat.', true);
            }
        }

        function closeCaseChatModal() {
            if (!caseChatModal) return;

            activeCaseConversationCode = '';
            if (caseConversationRequestController) {
                caseConversationRequestController.abort();
                caseConversationRequestController = null;
            }
            setCaseChatModal(false);
            caseChatModalState?.classList.remove('is-error');
            if (caseChatModalState) {
                caseChatModalState.classList.remove('d-none');
                caseChatModalState.innerHTML = `
                    <i class="fa-solid fa-spinner fa-spin" aria-hidden="true"></i>
                    <span>Cargando conversación del caso...</span>`;
            }
            if (caseChatModalFrame) {
                caseChatModalFrame.onload = null;
                caseChatModalFrame.classList.add('d-none');
                caseChatModalFrame.removeAttribute('src');
                caseChatModalFrame.removeAttribute('srcdoc');
            }
            if (btnCaseChatOpenDetails) {
                btnCaseChatOpenDetails.setAttribute('href', '#');
            }
        }

        function animateCardText(el, newText) {
            if (!el || el.textContent === newText) return Promise.resolve();
            return new Promise(resolve => {
                el.classList.add('card-text-exit');
                setTimeout(() => {
                    el.textContent = newText;
                    el.classList.remove('card-text-exit');
                    el.classList.add('card-text-enter');
                    // Force reflow so the enter class applies before we remove it
                    void el.offsetWidth;
                    el.classList.remove('card-text-enter');
                    resolve();
                }, 180);
            });
        }

        function getSelectedFiles() {
            return state.selectedFiles.slice();
        }

        function getFileIdentity(file) {
            return `${file.name}__${file.size}__${file.lastModified}`;
        }

        function syncInputFromSelectedFiles() {
            if (!filesInput) return;

            const dt = new DataTransfer();
            state.selectedFiles.forEach(file => dt.items.add(file));
            filesInput.files = dt.files;
        }

        function mergeSelectedFiles(newFiles) {
            const incoming = Array.from(newFiles || []);
            if (!incoming.length) return;

            resetCreatedCaseState();
            resetWorkflowUi();
            const existingKeys = new Set(state.selectedFiles.map(getFileIdentity));
            incoming.forEach(file => {
                const key = getFileIdentity(file);
                if (!existingKeys.has(key)) {
                    state.selectedFiles.push(file);
                    existingKeys.add(key);
                }
            });

            syncInputFromSelectedFiles();
        }

        function removeSelectedFile(index) {
            resetCreatedCaseState();
            state.selectedFiles = state.selectedFiles.filter((_, currentIndex) => currentIndex !== index);
            syncInputFromSelectedFiles();
        }

        function resetCreatedCaseState() {
            state.lastCreatedCase = null;
            state.isOpeningCreatedCase = false;
            hideCreationSuccessActions();
        }

        function showWorkflowModal() {
            workflowModal?.classList.remove('d-none');
            workflowModal?.setAttribute('aria-hidden', 'false');
            document.body.classList.add('workflow-modal-open');
        }

        function hideWorkflowModal() {
            workflowModal?.classList.add('d-none');
            workflowModal?.setAttribute('aria-hidden', 'true');
            document.body.classList.remove('workflow-modal-open');
        }

        function resetWorkflowUi() {
            clearDraftRow();
            hideWorkflowModal();
            hideAiPanel();
            workflowEl?.classList.add('d-none');
            workflowLog.innerHTML = '';
            workflowBadge.textContent = 'En progreso';
            workflowBadge.className = 'workflow-badge';
            workflowTitle.textContent = 'Preparando caso';
            workflowDetail.textContent = 'Validando el proceso y los documentos seleccionados.';
            if (workflowProgressCard) workflowProgressCard.classList.remove('is-error', 'is-success');
            if (workflowCardBar) workflowCardBar.style.width = '0%';
            btnCloseWorkflowModal?.classList.add('d-none');
        }

        function setFilterBusy(on, text = 'Consultando expedientes...') {
            tableShell?.classList.toggle('is-filtering', Boolean(on));

            if (window.jQuery && casesSection) {
                try {
                    const $section = window.jQuery(casesSection);
                    if (on) {
                        $section.waitMe({
                            effect: 'bounce',
                            text,
                            bg: 'rgba(255,255,255,0.72)',
                            color: '#1d4ed8',
                            fontSize: '14px'
                        });
                    } else {
                        $section.waitMe('hide');
                    }
                } catch {
                    // noop
                }
            }
        }

        function startPageLoading(text = 'Consultando expedientes...') {
            if (typeof window.cargando === 'function') {
                window.cargando();
                return;
            }

            if (window.jQuery && casesSection) {
                try {
                    window.jQuery(casesSection).waitMe({
                        effect: 'bounce',
                        text,
                        bg: 'rgba(255,255,255,0.72)',
                        color: '#1d4ed8',
                        fontSize: '15px'
                    });
                } catch {
                    // noop
                }
            }
        }

        function preserveTableViewport() {
            const currentY = window.scrollY;
            window.requestAnimationFrame(() => window.scrollTo({ top: currentY, behavior: 'auto' }));
        }

        function rememberCasesAnchor() {
            try {
                window.sessionStorage.setItem('nexus.index.anchor', 'casesSection');
            } catch {
                // noop
            }
        }

        function shouldRestoreCasesAnchor() {
            if (window.location.hash === '#casesSection') {
                return true;
            }

            try {
                return window.sessionStorage.getItem('nexus.index.anchor') === 'casesSection';
            } catch {
                return false;
            }
        }

        function clearCasesAnchor() {
            try {
                window.sessionStorage.removeItem('nexus.index.anchor');
            } catch {
                // noop
            }
        }

        function scrollToCasesSection(behavior = 'auto') {
            if (!casesSection) return;

            window.requestAnimationFrame(() => {
                casesSection.scrollIntoView({ behavior, block: 'start' });
            });
        }

        function setQueryParam(url, key, value) {
            const normalizedValue = (value ?? '').toString().trim();
            if (normalizedValue) {
                url.searchParams.set(key, normalizedValue);
            } else {
                url.searchParams.delete(key);
            }
        }

        function buildIndexUrl(overrides = {}) {
            const url = new URL(config.indexUrl || window.location.pathname, window.location.origin);
            const nextPage = overrides.page ?? 1;
            const nextPageSize = overrides.pageSize ?? pageSizeSelect?.value ?? config.pageSize ?? 10;

            url.searchParams.set('page', String(nextPage));
            url.searchParams.set('pageSize', String(nextPageSize));

            if (Number(config.windowDays || 0) > 0) {
                url.searchParams.set('windowDays', String(config.windowDays));
            }

            setQueryParam(url, 'search', overrides.search ?? filterSearch?.value);
            setQueryParam(url, 'status', overrides.status ?? filterStatus?.value);
            setQueryParam(url, 'type', overrides.type ?? filterType?.value);
            setQueryParam(url, 'process', overrides.process ?? filterProcess?.value);
            setQueryParam(url, 'period', overrides.period ?? filterPeriod?.value);

            url.hash = 'casesSection';
            return url;
        }

        function navigateToFilteredPage(overrides = {}, loadingText = 'Consultando expedientes...') {
            if (state.filterTimer) {
                window.clearTimeout(state.filterTimer);
                state.filterTimer = null;
            }

            rememberCasesAnchor();
            startPageLoading(loadingText);
            window.location.href = buildIndexUrl(overrides).toString();
        }

        function scheduleFilterNavigation(reason = 'Buscando expedientes...', overrides = {}) {
            if (state.filterTimer) {
                window.clearTimeout(state.filterTimer);
            }

            state.filterTimer = window.setTimeout(() => {
                state.filterTimer = null;
                navigateToFilteredPage({ page: 1, ...overrides }, reason);
            }, 260);
        }

        function updateCreatedCaseRow(payload, caseCode) {
            const row = q(`.case-row[data-case-code="${caseCode}"]`, casesTableBody);
            if (!row) return;

            const caseLabel = row.dataset.caseLabel || buildShortCaseCode(caseCode);
            row.classList.remove('is-processing');
            row.dataset.status = 'evaluado';
            row.dataset.typeLabel = payload.typeCase || 'No Definido';
            row.dataset.search = buildRowSearch(caseLabel, row.dataset.processName, row.dataset.typeLabel, q('.case-date', row)?.textContent || '');
            q('[data-cell="status"]', row).innerHTML = getStatusHtml('evaluado');
            q('[data-cell="type"]', row).innerHTML = getTypeHtml(payload.typeCase || 'No Definido');
            q('[data-cell="actions"]', row).innerHTML = getActionsHtml(caseCode, caseLabel, true);
        }

        function finalizeProcessedRow(row, payload, previousStatus) {
            if (!row) return;

            const caseCode = row.dataset.caseCode;
            const caseLabel = row.dataset.caseLabel || buildShortCaseCode(caseCode);

            stopRowProcessing(row);
            row.classList.remove('is-processing');
            row.classList.add('case-row-highlight');
            row.dataset.status = 'evaluado';
            row.dataset.typeLabel = payload.typeCase || 'No Definido';
            row.dataset.search = buildRowSearch(caseLabel, row.dataset.processName, row.dataset.typeLabel, q('.case-date', row)?.textContent || '');

            if (previousStatus !== 'evaluado') {
                state.metrics.evaluated += 1;
                state.metrics.pending = Math.max(0, state.metrics.pending - 1);
            }

            q('[data-cell="status"]', row).innerHTML = getStatusHtml('evaluado');
            q('[data-cell="type"]', row).innerHTML = getTypeHtml(payload.typeCase || 'No Definido');
            q('[data-cell="actions"]', row).innerHTML = getActionsHtml(caseCode, caseLabel, true);

            window.setTimeout(() => row.classList.remove('case-row-highlight'), 2200);
        }

        function failProcessedRow(row, error) {
            if (!row) return;

            const caseCode = row.dataset.caseCode;
            const caseLabel = row.dataset.caseLabel || buildShortCaseCode(caseCode);
            stopRowProcessing(row);
            row.classList.remove('is-processing');
            row.dataset.status = 'error';
            row.dataset.typeLabel = 'Error';
            row.dataset.search = buildRowSearch(caseLabel, row.dataset.processName, 'Error', q('.case-date', row)?.textContent || '');

            q('[data-cell="status"]', row).innerHTML = getStatusHtml('error');
            q('[data-cell="type"]', row).innerHTML = createProgressCardHtml('Error operativo', 'No fue posible completar el procesamiento.', 100, 'error');
            q('[data-cell="actions"]', row).innerHTML = getActionsHtml(caseCode, caseLabel, false);
        }

        async function processCaseRequest(caseCode) {
            const form = new FormData();
            form.append('caseCode', caseCode);

            const response = await fetch(config.processCaseUrl || '/Nexus/ProcessCaseAjax', {
                method: 'POST',
                body: form
            });

            const payload = await readResponsePayload(response);
            if (!response.ok) {
                throw new Error(payload.message || 'No se pudo procesar el caso.');
            }

            return payload;
        }

        async function runCaseProcessing(caseCode, options = {}) {
            const row = options.row || q(`.case-row[data-case-code="${caseCode}"]`, casesTableBody);
            const previousStatus = row?.dataset.status || 'pendiente';

            if (row) {
                stopRowProcessing(row);
                let phaseIndex = 0;
                paintRowProcessing(row, phaseIndex);
                const timer = setInterval(() => {
                    phaseIndex = (phaseIndex + 1) % rowProcessingPhases.length;
                    paintRowProcessing(row, phaseIndex);
                }, 1500);
                state.rowProgressTimers.set(caseCode, timer);
            }

            if (typeof options.onStart === 'function') {
                options.onStart(row);
            }

            try {
                const payload = await processCaseRequest(caseCode);

                if (row) {
                    finalizeProcessedRow(row, payload, previousStatus);
                } else if (options.treatAsEvaluated) {
                    state.metrics.evaluated += 1;
                    state.metrics.pending = Math.max(0, state.metrics.pending - 1);
                }

                if (typeof options.onSuccess === 'function') {
                    await options.onSuccess(payload, row, previousStatus);
                }

                syncCasesTable();
                return payload;
            } catch (error) {
                if (row) {
                    failProcessedRow(row, error);
                }

                syncCasesTable();

                if (typeof options.onError === 'function') {
                    await options.onError(error, row);
                    return null;
                }

                throw error;
            }
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

        function hideCreationSuccessActions() {
            creationSuccessActions?.classList.add('d-none');
            if (btnGoCreatedCase) {
                btnGoCreatedCase.setAttribute('href', '#');
                btnGoCreatedCase.classList.remove('disabled');
                btnGoCreatedCase.removeAttribute('aria-disabled');
                btnGoCreatedCase.innerHTML = '<i class="fa-solid fa-arrow-up-right-from-square me-2"></i>Ir al caso';
            }
            if (btnStayOnIndex) {
                btnStayOnIndex.disabled = false;
            }
        }

        function showCreationSuccessActions(caseData) {
            if (!creationSuccessActions || !btnGoCreatedCase) return;

            const caseLabel = buildShortCaseCode(caseData.caseCode);
            state.lastCreatedCase = caseData;
            creationSuccessTitle.textContent = `${caseLabel} listo para auditoria`;
            creationSuccessDetail.textContent = `El expediente ${caseLabel} ya quedó registrado en ${caseData.nameProccess || state.selectedProcessName || 'Nexus'}.`;
            btnGoCreatedCase.setAttribute('href', (config.detailsUrlTemplate || '/Nexus/Details1?caseCode=__CASE__').replace('__CASE__', encodeURIComponent(caseData.caseCode)));
            creationSuccessActions.classList.remove('d-none');
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
                    removeSelectedFile(index);
                    renderPreviewGallery();
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

            resetCreatedCaseState();
            resetWorkflowUi();
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
            qa('.workflow-log-item', workflowLog).slice(4).forEach(entry => entry.remove());
        }

        function paintCreationPhase(index, mode = 'progress') {
            const currentPhase = creationPhases[index] || creationPhases[0];
            animateCardText(workflowTitle, currentPhase.title);
            animateCardText(workflowDetail, currentPhase.detail);

            const progress = mode === 'success'
                ? 100
                : Math.min(92, 18 + (index * 15));

            if (mode === 'error') {
                paintDraftRow('Creacion interrumpida', currentPhase.detail, progress, 'error');
            } else {
                paintDraftRow(currentPhase.title, currentPhase.detail, progress);
            }

            animateCardText(workflowCardTitle, currentPhase.title);
            animateCardText(workflowCardDetail, currentPhase.detail);
            if (workflowCardBar) workflowCardBar.style.width = progress + '%';

            if (workflowProgressCard) {
                workflowProgressCard.classList.remove('is-error', 'is-success');
                if (mode === 'error') workflowProgressCard.classList.add('is-error');
                if (mode === 'success') workflowProgressCard.classList.add('is-success');
            }
        }

        function startCreationWorkflow(context) {
            resetCreationWorkflow();
            ensureDraftRow(context);
            showWorkflowModal();
            workflowEl.classList.remove('d-none');
            btnCloseWorkflowModal?.classList.add('d-none');
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
                    showCreationSuccessActions(caseData);
                    btnCloseWorkflowModal?.classList.remove('d-none');
                    await new Promise(resolve => window.setTimeout(resolve, 240));
                },
                fail(message) {
                    if (state.creationInterval) {
                        clearInterval(state.creationInterval);
                        state.creationInterval = null;
                    }

                    paintCreationPhase(Math.max(0, creationPhases.length - 2), 'error');
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
                    btnCloseWorkflowModal?.classList.remove('d-none');
                }
            };
        }

        function updateMetrics() {
            if (metricTotalCases) metricTotalCases.textContent = state.metrics.total;
            if (metricEvaluatedCases) metricEvaluatedCases.textContent = state.metrics.evaluated;
            if (metricPendingCases) metricPendingCases.textContent = state.metrics.pending;
            if (metricProcessCount) metricProcessCount.textContent = state.metrics.processCount;
            refreshPaginationMeta();
        }

        function refreshPaginationMeta() {
            if (!paginationMetaTitle || !paginationMetaDetail) return;

            const total = Number(state.metrics.total || 0);
            const pageSize = Number(pageSizeSelect?.value || config.pageSize || 10);
            const totalPages = Math.max(1, Math.ceil(total / pageSize));
            const currentPage = Math.min(Number(config.currentPage || 1), totalPages);
            const pageStart = total === 0 ? 0 : ((currentPage - 1) * pageSize) + 1;
            const pageEnd = total === 0 ? 0 : Math.min(total, currentPage * pageSize);

            paginationMetaTitle.textContent = total === 0
                ? 'Sin casos registrados'
                : `Mostrando ${pageStart}-${pageEnd} de ${total} casos`;
            paginationMetaDetail.textContent = Number(config.windowDays || 0) > 0
                ? `Página ${currentPage} de ${totalPages} · últimos ${config.windowDays} días`
                : `Página ${currentPage} de ${totalPages} · historial disponible`;
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
                const searchHaystack = normalize(row.dataset.search || row.textContent || '');
                const rowStatus = normalize(row.dataset.status || '');
                const rowType = normalize(row.dataset.typeLabel || '');
                const rowProcess = normalize(row.dataset.processName || '');
                const matchesSearch = !term || searchHaystack.includes(term);
                const matchesStatus = !statusValue || rowStatus === statusValue;
                const matchesType = !typeValue || rowType.includes(typeValue);
                const matchesProcess = !processValue || rowProcess.includes(processValue);
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
            preserveTableViewport();
            setFilterBusy(false);
        }

        function navigateWithPageSize(pageSize) {
            const nextSize = Number(pageSize || config.pageSize || 10);
            navigateToFilteredPage({ page: 1, pageSize: nextSize }, 'Actualizando cantidad de casos...');
        }

        function clearUploadSelection(options = {}) {
            if (!options.keepWorkflow) {
                resetCreatedCaseState();
                resetWorkflowUi();
            }
            state.selectedFiles = [];
            syncInputFromSelectedFiles();
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

        async function processCase(button, options = {}) {
            if (!button || button.disabled) return;

            const row = button.closest('tr');
            const caseCode = button.dataset.casecode;
            if (!caseCode) return;

            try {
                await runCaseProcessing(caseCode, {
                    row,
                    onSuccess: options.onSuccess,
                    onStart: options.onStart,
                    onError: options.onError
                });
            } catch (error) {
                console.error(error);
                if (!options.suppressAlert) {
                    Swal.fire({
                        icon: 'warning',
                        title: 'Advertencia',
                        text: error.message || 'No se pudo procesar el caso.'
                    });
                }
            }
        }

        const aiPhases = [
            { icon: 'fa-file-arrow-up',    text: 'Enviando expediente a la IA',        detail: 'Preparando el paquete documental para su análisis...',       progress: 14 },
            { icon: 'fa-magnifying-glass',  text: 'Leyendo y estructurando documentos', detail: 'El motor OCR está procesando el contenido extraído...',        progress: 30 },
            { icon: 'fa-brain-circuit',     text: 'Modelo de IA analizando',            detail: 'Identificando entidades, fechas y datos clave del caso...',    progress: 50 },
            { icon: 'fa-wand-magic-sparkles', text: 'Generando respuesta inicial',      detail: 'Construyendo el análisis preliminar del expediente...',        progress: 70 },
            { icon: 'fa-shield-check',      text: 'Validando resultados',               detail: 'Verificando coherencia y completitud de la respuesta...',      progress: 88 },
            { icon: 'fa-door-open',         text: 'Abriendo caso',                      detail: 'Todo listo. Redirigiendo al detalle del expediente...',        progress: 100 }
        ];

        function showAiPanel() {
            if (!aiAnalysisPanel) return;
            hideCreationSuccessActions();
            btnCloseWorkflowModal?.classList.add('d-none');
            workflowBadge.textContent = 'Analizando con IA';
            workflowBadge.className = 'workflow-badge is-ai';
            if (aiProgressCard) aiProgressCard.classList.remove('is-error', 'is-success');
            if (aiCardBar) aiCardBar.style.width = '0%';
            aiAnalysisPanel.classList.remove('d-none');
        }

        function hideAiPanel() {
            aiAnalysisPanel?.classList.add('d-none');
        }

        function updateAiStep(index) {
            const phase = aiPhases[index];
            if (!phase) return;
            animateCardText(aiCardTitle, phase.text);
            animateCardText(aiCardDetail, phase.detail);
            if (aiCardBar) aiCardBar.style.width = phase.progress + '%';
            if (aiProgressCard) {
                aiProgressCard.classList.remove('is-error', 'is-success');
                if (index === aiPhases.length - 1) aiProgressCard.classList.add('is-success');
            }
        }

        async function openCreatedCase() {
            if (!state.lastCreatedCase || state.isOpeningCreatedCase) return;

            state.isOpeningCreatedCase = true;
            btnGoCreatedCase?.classList.add('disabled');
            btnGoCreatedCase?.setAttribute('aria-disabled', 'true');
            if (btnStayOnIndex) btnStayOnIndex.disabled = true;

            showAiPanel();
            updateAiStep(0);
            appendWorkflowLog('Iniciando análisis con inteligencia artificial.', 'info');

            let phaseIndex = 0;
            const phaseTimer = setInterval(() => {
                if (phaseIndex < aiPhases.length - 2) {
                    phaseIndex++;
                    updateAiStep(phaseIndex);
                }
            }, 1100);

            try {
                await runCaseProcessing(state.lastCreatedCase.caseCode, {
                    treatAsEvaluated: true,
                    suppressAlert: true,
                    onStart: () => {
                        appendWorkflowLog('Motor de IA procesando el expediente.', 'info');
                    },
                    onSuccess: async () => {
                        clearInterval(phaseTimer);
                        updateAiStep(aiPhases.length - 1);
                        updateMetrics();
                        appendWorkflowLog('Análisis completado. Abriendo expediente.', 'success');
                        await new Promise(r => window.setTimeout(r, 600));
                        const targetUrl = (config.detailsUrlTemplate || '/Nexus/Details1?caseCode=__CASE__')
                            .replace('__CASE__', encodeURIComponent(state.lastCreatedCase.caseCode));
                        startPageLoading('Abriendo expediente...');
                        resetCreationWorkflow();
                        window.location.href = targetUrl;
                    },
                    onError: async error => {
                        clearInterval(phaseTimer);
                        console.error(error);
                        state.isOpeningCreatedCase = false;
                        hideAiPanel();
                        btnCloseWorkflowModal?.classList.remove('d-none');
                        workflowBadge.textContent = 'Error';
                        workflowBadge.className = 'workflow-badge is-error';
                        if (state.lastCreatedCase) showCreationSuccessActions(state.lastCreatedCase);
                        appendWorkflowLog(error.message || 'No se pudo procesar el caso.', 'error');
                        Swal.fire({
                            icon: 'warning',
                            title: 'Advertencia',
                            text: error.message || 'No se pudo preparar el caso antes de abrirlo.'
                        });
                    }
                });
            } catch (error) {
                clearInterval(phaseTimer);
                console.error(error);
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
            mergeSelectedFiles(event.dataTransfer.files || []);
            renderPreviewGallery();
        });

        filesInput?.addEventListener('change', event => {
            mergeSelectedFiles(event.target.files || []);
            filesInput.value = '';
            renderPreviewGallery();
        });

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
                state.metrics.total += 1;
                state.metrics.pending += 1;

                if (Number(config.currentPage || 1) === 1) {
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

                    const liveRows = qa('.case-row', casesTableBody)
                        .filter(row => row.dataset.transient !== 'true');
                    const maxRows = Number(pageSizeSelect?.value || config.pageSize || 10);
                    if (liveRows.length > maxRows) {
                        liveRows[liveRows.length - 1]?.remove();
                    }
                } else {
                    clearDraftRow();
                }

                clearUploadSelection({ keepWorkflow: true });
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

        filterSearch?.addEventListener('input', () => scheduleFilterNavigation('Buscando expedientes...'));
        [filterStatus, filterType, filterProcess].forEach(control => {
            control?.addEventListener('change', () => navigateToFilteredPage({ page: 1 }, 'Aplicando filtros...'));
        });
        filterPeriod?.addEventListener('change', () => navigateToFilteredPage({ page: 1 }, 'Buscando por periodo...'));

        btnResetFilters?.addEventListener('click', () => {
            clearAllFilters();
            navigateToFilteredPage({ page: 1 }, 'Limpiando filtros...');
        });
        btnResetFiltersInline?.addEventListener('click', () => {
            clearAllFilters();
            navigateToFilteredPage({ page: 1 }, 'Limpiando filtros...');
        });
        pageSizeSelect?.addEventListener('change', event => {
            navigateWithPageSize(event.target.value);
        });

        activeFilterChips?.addEventListener('click', event => {
            const chip = event.target.closest('.active-filter-chip');
            if (!chip) return;

            const key = chip.dataset.filterKey;
            if (key === 'search') filterSearch.value = '';
            if (key === 'status') filterStatus.value = '';
            if (key === 'type') filterType.value = '';
            if (key === 'process') filterProcess.value = '';
            if (key === 'period' && filterPeriod) filterPeriod.value = '';
            navigateToFilteredPage({ page: 1 }, 'Actualizando resultados...');
        });

        btnStayOnIndex?.addEventListener('click', () => {
            resetCreationWorkflow();
            uploadArea?.scrollIntoView({ behavior: 'smooth', block: 'center' });
        });

        btnCloseWorkflowModal?.addEventListener('click', () => {
            resetCreationWorkflow();
        });

        btnGoCreatedCase?.addEventListener('click', event => {
            event.preventDefault();
            openCreatedCase();
        });

        qa('.table-pagination-nav a[href]', document).forEach(link => {
            const href = link.getAttribute('href');
            if (!href || href === '#') return;
            try {
                const url = new URL(href, window.location.origin);
                url.hash = 'casesSection';
                link.setAttribute('href', url.toString());
                link.addEventListener('click', () => {
                    rememberCasesAnchor();
                    startPageLoading('Cargando casos...');
                });
            } catch {
                // noop
            }
        });

        workflowModal?.addEventListener('click', event => {
            if (!event.target.classList.contains('creation-workflow-modal-backdrop')) return;
            if (btnCloseWorkflowModal?.classList.contains('d-none')) return;
            resetCreationWorkflow();
        });

        caseDocumentsModal?.addEventListener('click', event => {
            if (!event.target.classList.contains('case-documents-modal-backdrop')) return;
            closeCaseDocumentsModal();
        });

        caseChatModal?.addEventListener('click', event => {
            if (!event.target.classList.contains('case-chat-modal-backdrop')) return;
            closeCaseChatModal();
        });

        window.addEventListener('keydown', event => {
            if (event.key !== 'Escape') return;
            if (caseDocumentsModal && !caseDocumentsModal.classList.contains('d-none')) {
                closeCaseDocumentsModal();
                return;
            }
            if (caseChatModal && !caseChatModal.classList.contains('d-none')) {
                closeCaseChatModal();
                return;
            }
            if (!workflowModal || workflowModal.classList.contains('d-none')) return;
            if (btnCloseWorkflowModal?.classList.contains('d-none')) return;
            resetCreationWorkflow();
        });

        btnCloseCaseDocumentsModal?.addEventListener('click', () => {
            closeCaseDocumentsModal();
        });

        btnCloseCaseChatModal?.addEventListener('click', () => {
            closeCaseChatModal();
        });

        document.addEventListener('click', event => {
            const processButton = event.target.closest('.btnProcessRow');
            if (processButton) {
                processCase(processButton);
            }

            const chatButton = event.target.closest('.btnShowCaseChat');
            if (chatButton) {
                const row = chatButton.closest('.case-row');
                const caseCode = chatButton.dataset.casecode || row?.dataset.caseCode;
                const caseLabel = chatButton.dataset.caselabel || row?.dataset.caseLabel || buildShortCaseCode(caseCode);
                openCaseChatModal(caseCode, caseLabel);
                return;
            }

            const detailsButton = event.target.closest('.btnViewDetails');
            if (detailsButton) {
                window.handleViewDetails(detailsButton);
                return;
            }

            const documentsButton = event.target.closest('.btnShowCaseDocuments');
            if (documentsButton) {
                const row = documentsButton.closest('.case-row');
                const caseCode = documentsButton.dataset.casecode || row?.dataset.caseCode;
                const caseLabel = documentsButton.dataset.caselabel || row?.dataset.caseLabel || buildShortCaseCode(caseCode);
                openCaseDocumentsModal(caseCode, caseLabel);
                return;
            }

            const zipButton = event.target.closest('.btnDownloadCaseZip');
            if (zipButton) {
                const row = zipButton.closest('.case-row');
                const caseCode = zipButton.dataset.casecode || row?.dataset.caseCode;
                const caseLabel = zipButton.dataset.caselabel || row?.dataset.caseLabel || buildShortCaseCode(caseCode);

                downloadCaseDocumentsZip(caseCode, caseLabel, zipButton).catch(error => {
                    console.error(error);
                    Swal.fire({
                        icon: 'warning',
                        title: 'Advertencia',
                        text: error?.message || 'No se pudo descargar el ZIP del caso.'
                    });
                });
            }
        });

        window.addEventListener('pageshow', event => {
            if (event.persisted) {
                window.location.reload();
                return;
            }
            resetCreationWorkflow();
            if (shouldRestoreCasesAnchor()) {
                scrollToCasesSection('auto');
                clearCasesAnchor();
                return;
            }
            preserveTableViewport();
        });

        loadProcessButtons();
        syncInputFromSelectedFiles();
        renderPreviewGallery();
        syncCasesTable();
        if (shouldRestoreCasesAnchor()) {
            scrollToCasesSection('auto');
            clearCasesAnchor();
        }
    });
})();
