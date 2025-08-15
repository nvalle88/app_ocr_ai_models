

document.addEventListener("DOMContentLoaded", function () {
(function () {
    let __pdfRenderToken = 0;
    let __typeToken = 0;
    let __thinkingToken = 0;
    let __currentThinking = null;

    function q(sel) { return document.querySelector(sel); }
    function qa(sel) { return Array.from(document.querySelectorAll(sel)); }

    function setBtnLoading(on) {
        const btn = q('#sendChat'); if (!btn) return;
        const sendIcon = btn.querySelector('.send-icon');
        const spinner = btn.querySelector('.spinner-icon');
        if (on) {
            btn.classList.add('loading');
            btn.setAttribute('aria-busy', 'true');
            if (sendIcon) sendIcon.classList.add('visually-hidden');
            if (spinner) spinner.classList.remove('visually-hidden');
        } else {
            btn.classList.remove('loading');
            btn.setAttribute('aria-busy', 'false');
            if (sendIcon) sendIcon.classList.remove('visually-hidden');
            if (spinner) spinner.classList.add('visually-hidden');
        }
    }

    function ensureThinkingOverlay() {
        let overlay = document.getElementById('thinkingOverlay');
        if (!overlay) {
            overlay = document.createElement('div');
            overlay.id = 'thinkingOverlay';
            overlay.className = 'thinking-overlay';
            overlay.setAttribute('aria-hidden', 'true');
            overlay.innerHTML = '<div class="thinking-icon"><i class="fa-solid fa-brain" aria-hidden="true"></i></div><div class="thinking-text" id="thinkingText">Pensando...</div>';
            document.body.appendChild(overlay);
        }
        return overlay;
    }

    function startThinking() {
        if (__currentThinking && typeof __currentThinking.cleanup === 'function') {
            try { __currentThinking.cleanup(); } catch (e) { }
            __currentThinking = null;
        }

        __thinkingToken++;
        const myToken = __thinkingToken;

        const phrases = [
            'Pensando',
            'Analizando',
            'Revisando fuentes',
            'Formulando respuesta',
            'Preparando',
            'Comprobando',
            'Sintetizando'
        ];

        const overlay = ensureThinkingOverlay();
        const textEl = overlay.querySelector('#thinkingText');

        overlay.classList.add('visible');
        overlay.setAttribute('aria-hidden', 'false');

        const dotIntervalMs = 420;
        const changePhraseMs = 2200;
        const changeEveryTicks = Math.max(1, Math.round(changePhraseMs / dotIntervalMs));

        let currentPhrase = phrases[Math.floor(Math.random() * phrases.length)];
        let dots = 0;
        let tick = 0;
        if (textEl) textEl.textContent = currentPhrase + '...';

        const interval = setInterval(() => {
            if (myToken !== __thinkingToken) return;
            tick++;
            dots = (dots + 1) % 4;
            if (tick % changeEveryTicks === 0) {
                let next = currentPhrase;
                if (phrases.length > 1) {
                    while (next === currentPhrase) {
                        next = phrases[Math.floor(Math.random() * phrases.length)];
                    }
                } else {
                    next = phrases[0];
                }
                currentPhrase = next;
            }
            if (textEl) textEl.textContent = currentPhrase + (dots ? '.'.repeat(dots) : '');
        }, dotIntervalMs);

        const fallbackTimeout = setTimeout(() => {
            try { cleanup(); } catch (e) { }
        }, 30000);

        function cleanup() {
            try { clearInterval(interval); } catch (e) { }
            try { clearTimeout(fallbackTimeout); } catch (e) { }
            try { overlay.classList.remove('visible'); overlay.setAttribute('aria-hidden', 'true'); } catch (e) { }
            if (__currentThinking && __currentThinking.token === myToken) __currentThinking = null;
        }

        __currentThinking = {
            token: myToken,
            cleanup,
            interval,
            fallbackTimeout
        };

        return __currentThinking;
    }

    function stopThinking(tokenObj) {
        if (tokenObj && typeof tokenObj.cleanup === 'function') {
            try { tokenObj.cleanup(); } catch (e) { }
            if (__currentThinking && __currentThinking.token === tokenObj.token) __currentThinking = null;
            return;
        }
        if (__currentThinking && typeof __currentThinking.cleanup === 'function') {
            try { __currentThinking.cleanup(); } catch (e) { }
            __currentThinking = null;
            return;
        }
        const overlay = document.getElementById('thinkingOverlay');
        if (overlay) {
            try { overlay.classList.remove('visible'); overlay.setAttribute('aria-hidden', 'true'); } catch (e) { }
        }
        __currentThinking = null;
    }

    function positionChatInput() {
        const centerCol = q('#colCenter');
        const chatInner = q('#chatInputInner');
        const footer = q('#nexusFooter');
        if (!centerCol || !chatInner || !footer) return;

        const rect = centerCol.getBoundingClientRect();
        const footerH = Math.ceil(footer.getBoundingClientRect().height || 0);
        const gap = 8;

        const vw = window.innerWidth || document.documentElement.clientWidth;
        if (vw <= 991) {
            chatInner.style.left = '8px';
            chatInner.style.width = (vw - 16) + 'px';
            chatInner.style.maxWidth = (vw - 16) + 'px';
        } else {
            const left = Math.max(8, rect.left + 8);
            const width = Math.max(300, rect.width - 16);
            chatInner.style.left = left + 'px';
            chatInner.style.width = width + 'px';
            chatInner.style.maxWidth = width + 'px';
        }
        chatInner.style.position = 'fixed';
        chatInner.style.bottom = (footerH + gap) + 'px';
        chatInner.style.zIndex = 1300;

        const computedH = chatInner.getBoundingClientRect().height || 72;
        document.documentElement.style.setProperty('--chat-input-height', computedH + 'px');
        qa('.result-area').forEach(el => el.style.paddingBottom = (computedH + 28) + 'px');
    }

    function stabilizePositionDuringTransition(timeout = 700, threshold = 2, stableFrames = 3) {
        const chatInner = q('#chatInputInner');
        const centerCol = q('#colCenter');
        if (!chatInner || !centerCol) return Promise.resolve(false);

        let lastLeft = null, lastWidth = null, stableCount = 0;
        const start = performance.now();

        return new Promise(resolve => {
            function step() {
                const rect = centerCol.getBoundingClientRect();
                const vw = window.innerWidth || document.documentElement.clientWidth;
                let desiredLeft, desiredWidth;
                if (vw <= 991) {
                    desiredLeft = 8;
                    desiredWidth = Math.max(300, vw - 16);
                } else {
                    desiredLeft = Math.max(8, rect.left + 8);
                    desiredWidth = Math.max(300, rect.width - 16);
                }

                if (lastLeft === null) { lastLeft = desiredLeft; lastWidth = desiredWidth; stableCount = 1; }
                else {
                    const dl = Math.abs(desiredLeft - lastLeft);
                    const dw = Math.abs(desiredWidth - lastWidth);
                    if (dl <= threshold && dw <= threshold) stableCount++; else stableCount = 1;
                    lastLeft = desiredLeft; lastWidth = desiredWidth;
                }

                chatInner.style.left = desiredLeft + 'px';
                chatInner.style.width = desiredWidth + 'px';
                chatInner.style.maxWidth = desiredWidth + 'px';
                positionChatInput();

                if (stableCount >= stableFrames) return resolve(true);
                if (performance.now() - start > timeout) return resolve(false);
                requestAnimationFrame(step);
            }
            requestAnimationFrame(step);
        });
    }

    document.addEventListener('DOMContentLoaded', () => {
        const textarea = q('#chatInput');
        const chatInner = q('#chatInputInner');
        const sendBtn = q('#sendChat');
        const chatOutputEl = q('#chatOutput');

        function autoResize(el) {
            if (!el) return;
            el.style.height = 'auto';
            const max = 320;
            const newH = Math.min(el.scrollHeight, max);
            el.style.height = newH + 'px';
            positionChatInput();
        }
        if (textarea) {
            autoResize(textarea);
            textarea.addEventListener('input', () => {
                stopThinking();
                setBtnLoading(false);
                autoResize(textarea);
            });

            textarea.addEventListener('focus', () => {
                if (chatInner) chatInner.classList.add('has-focus');
            });
            textarea.addEventListener('blur', () => {
                if (chatInner) chatInner.classList.remove('has-focus');
            });

            textarea.addEventListener('keydown', (e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    sendBtn.click();
                }
            });
        }

        async function typeWriteInto(el, html, speed = 6) {
            if (!el) return;
            __typeToken++;
            const myToken = __typeToken;
            el.setAttribute('data-content', html);
            const parentScroll = el.closest('.result-area') || el.parentElement;
            el.innerHTML = '';
            let i = 0;
            return new Promise(resolve => {
                function step() {
                    if (myToken !== __typeToken) return resolve();
                    i++;
                    el.innerHTML = html.slice(0, i);
                    try { if (parentScroll) parentScroll.scrollTop = parentScroll.scrollHeight; } catch (e) { }
                    if (i < html.length) setTimeout(step, speed); else resolve();
                }
                step();
            });
        }

        function sleep(ms) { return new Promise(res => setTimeout(res, ms)); }

        async function sendMessage() {
            const text = textarea?.value?.trim();
            if (!text) return;
            let thinking = null;
            try {
                setBtnLoading(true);
                thinking = startThinking();
                const minDelay = 700, maxDelay = 1600;
                const randomDelay = Math.floor(Math.random() * (maxDelay - minDelay + 1)) + minDelay;
                await sleep(randomDelay);

                await stabilizePositionDuringTransition(900);
                const resp = await fetch('@Url.Action("ChatAjax", "Nexus")', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ caseCode: '@Model.CaseCode', message: text })
                });

                stopThinking(thinking);
                setBtnLoading(false);

                if (!resp.ok) throw new Error(resp.statusText);
                const data = await resp.json();

                const html = marked.parse(data.response || '');
                await typeWriteInto(chatOutputEl, html, 6);

                textarea.value = ''; autoResize(textarea);
                try { textarea.focus({ preventScroll: true }); } catch (e) { textarea.focus(); }
            } catch (err) {
                console.error(err);
                stopThinking(thinking);
                setBtnLoading(false);
                alert('Error: ' + (err && err.message ? err.message : 'Error en la petición'));
            } finally {
                positionChatInput();
            }
        }
        if (sendBtn) sendBtn.addEventListener('click', sendMessage);

        if (chatOutputEl) {
            const content = chatOutputEl.getAttribute('data-content') || '';
            if (content) chatOutputEl.innerHTML = content;
        }

        function bindTableRows() {
            qa('.source-row').forEach(row => {
                const clone = row.cloneNode(true);
                row.parentNode.replaceChild(clone, row);
            });
            qa('.source-row').forEach(row => {
                row.addEventListener('click', (ev) => { ev.stopPropagation(); onSelectRow(row); });
                row.addEventListener('dblclick', (ev) => { ev.stopPropagation(); onSelectRow(row); const url = row.dataset.url; if (url) window.open(url, '_blank'); });
            });
            updateShownCount();
        }

        function updateShownCount() {
            const visible = qa('#sourcesTable .source-row').length;
            const shownEl = q('#shownCount');
            const leftCountEl = q('#leftCount');
            if (shownEl) shownEl.textContent = visible;
            if (leftCountEl) leftCountEl.textContent = visible;
        }

        const sourcesWrapper = q('#sourcesWrapper');
        const previewWrapper = q('#previewWrapper');
        const previewContent = q('#previewContent');
        const previewTitle = q('#previewTitle');
        const btnBackToList = q('#btnBackToList');
        const btnOpenInViewer = q('#btnOpenInViewer');
        const btnDownloadPreview = q('#btnDownloadPreview');
        const btnFlagPreview = q('#btnFlagPreview');
        let currentPreview = { url: '', type: '' };

        async function onSelectRow(row) {
            qa('.source-row.selected').forEach(r => r.classList.remove('selected'));
            row.classList.add('selected');

            const url = row.dataset.url;
            const type = (row.dataset.type || '').toLowerCase();
            const title = row.title || (row.querySelector('td:nth-child(2)')?.innerText) || 'Documento';
            previewTitle.textContent = title;

            if (sourcesWrapper) sourcesWrapper.style.display = 'none';
            if (previewWrapper) previewWrapper.style.display = 'block';

            currentPreview = { url: url, type: type };
            renderPreview(url, type);

            const leftCol = q('#colLeft'), centerCol = q('#colCenter'), rightCol = q('#colRight');
            if (leftCol && centerCol) {
                document.documentElement.classList.add('right-hidden');
                leftCol.style.flex = '0 0 70%'; leftCol.style.maxWidth = '70%'; leftCol.style.width = '70%';
                centerCol.style.flex = '0 0 30%'; centerCol.style.maxWidth = '30%'; centerCol.style.width = '30%';
                if (rightCol) rightCol.setAttribute('aria-hidden', 'true');
            }
            await stabilizePositionDuringTransition(1000);
            positionChatInput();
        }

        function backToList() {
            __pdfRenderToken++;
            if (previewContent) previewContent.innerHTML = '';
            currentPreview = { url: '', type: '' };
            if (previewWrapper) previewWrapper.style.display = 'none';
            if (sourcesWrapper) sourcesWrapper.style.display = 'block';
            qa('.source-row.selected').forEach(r => r.classList.remove('selected'));
            const leftCol = q('#colLeft'), centerCol = q('#colCenter'), rightCol = q('#colRight');
            if (leftCol && centerCol) {
                document.documentElement.classList.remove('right-hidden');
                leftCol.style.width = ''; leftCol.style.flex = ''; leftCol.style.maxWidth = '';
                centerCol.style.width = ''; centerCol.style.flex = ''; centerCol.style.maxWidth = '';
                if (rightCol) { rightCol.style.display = ''; rightCol.style.width = ''; rightCol.style.flex = ''; rightCol.style.maxWidth = ''; rightCol.setAttribute('aria-hidden', 'false'); }
            }
            setTimeout(() => { stabilizePositionDuringTransition(900).then(() => positionChatInput()); }, 40);
        }

        if (btnBackToList) btnBackToList.addEventListener('click', backToList);
        if (btnOpenInViewer) btnOpenInViewer.addEventListener('click', () => { if (!currentPreview.url) return alert('No hay documento en vista previa.'); window.open(currentPreview.url, '_blank'); });
        if (btnDownloadPreview) btnDownloadPreview.addEventListener('click', () => { if (!currentPreview.url) return alert('No hay documento seleccionado.'); const a = document.createElement('a'); a.href = currentPreview.url; a.download = currentPreview.url.split('/').pop() || 'download'; document.body.appendChild(a); a.click(); a.remove(); });
        if (btnFlagPreview) btnFlagPreview.addEventListener('click', () => { const selRow = q('.source-row.selected'); if (!selRow) return alert('Selecciona un documento primero.'); selRow.classList.toggle('marked'); });

        async function renderPreview(url, type) {
            __pdfRenderToken++;
            const myToken = __pdfRenderToken;
            if (!previewContent) return;
            previewContent.innerHTML = '';
            previewContent.scrollTop = 0;
            if (!url) {
                previewContent.innerHTML = '<p class="text-muted">No se encontró la URL del documento.</p>';
                return;
            }
            if (type !== 'pdf') {
                const img = document.createElement('img');
                img.src = url;
                img.className = 'img-fluid rounded preview-image';
                img.style.maxHeight = '100%';
                img.style.width = '100%';
                img.style.objectFit = 'contain';
                img.onerror = () => { previewContent.innerHTML = '<p class="text-danger">Error al cargar la imagen.</p>'; };
                previewContent.appendChild(img);
                img.onload = () => { stabilizePositionDuringTransition(400).then(() => positionChatInput()); };
                return;
            }

            const progressWrap = document.createElement('div');
            progressWrap.className = 'pdf-progress-wrap mb-2';
            progressWrap.innerHTML = `<div class="pdf-progress-label">Cargando PDF…</div>
                                                          <div class="progress"><div class="progress-bar" role="progressbar" style="width:0%"></div></div>`;
            previewContent.appendChild(progressWrap);

            try {
                const loadingTask = pdfjsLib.getDocument(url);
                const pdf = await loadingTask.promise;
                if (myToken !== __pdfRenderToken) { try { pdf.destroy?.(); } catch (e) { } return; }
                const total = pdf.numPages;
                const progressBar = progressWrap.querySelector('.progress-bar');
                const pagesContainer = document.createElement('div');
                pagesContainer.className = 'pdf-pages-container';
                previewContent.appendChild(pagesContainer);

                for (let p = 1; p <= total; p++) {
                    if (myToken !== __pdfRenderToken) { try { pdf.destroy?.(); } catch (e) { } return; }
                    const pageBox = document.createElement('div');
                    pageBox.className = 'pdf-page-box mb-3';
                    pageBox.setAttribute('data-page', p);
                    const loader = document.createElement('div');
                    loader.className = 'pdf-page-loader';
                    loader.innerText = `Cargando página ${p} de ${total}...`;
                    pageBox.appendChild(loader);
                    pagesContainer.appendChild(pageBox);

                    const page = await pdf.getPage(p);
                    const containerWidth = Math.max(300, previewContent.clientWidth || 800);
                    const unscaledViewport = page.getViewport({ scale: 1 });
                    const scale = Math.max(0.5, (containerWidth - 24) / unscaledViewport.width);
                    const viewport = page.getViewport({ scale });

                    const canvas = document.createElement('canvas');
                    canvas.width = viewport.width;
                    canvas.height = viewport.height;
                    canvas.style.width = '100%';
                    canvas.style.height = 'auto';
                    canvas.className = 'pdf-page-canvas rounded';

                    const ctx = canvas.getContext('2d');
                    try {
                        await page.render({ canvasContext: ctx, viewport: viewport }).promise;
                    } catch (errRender) {
                        console.error('Error renderizando página', p, errRender);
                        pageBox.innerHTML = `<div class="text-danger">Error al renderizar página ${p}</div>`;
                        continue;
                    }

                    pageBox.removeChild(loader);
                    pageBox.appendChild(canvas);
                    const caption = document.createElement('div');
                    caption.className = 'pdf-page-caption small text-muted mt-1';
                    caption.innerText = `Página ${p} / ${total}`;
                    pageBox.appendChild(caption);

                    const percent = Math.round((p / total) * 100);
                    if (progressBar) progressBar.style.width = percent + '%';
                    await new Promise(res => setTimeout(res, 20));
                }

                if (progressWrap && progressWrap.parentNode) progressWrap.parentNode.removeChild(progressWrap);
                if (previewContent) { stabilizePositionDuringTransition(500).then(() => positionChatInput()); }
            } catch (err) {
                console.error('Error al cargar PDF completo', err);
                previewContent.innerHTML = '<p class="text-danger">Error al cargar el PDF. Revisa CORS o la URL.</p>';
                stabilizePositionDuringTransition(400).then(() => positionChatInput());
            }
        }

        const btnRefresh = q('#btnRefreshSources');
        if (btnRefresh) btnRefresh.addEventListener('click', () => bindTableRows());
        bindTableRows();

        window.addEventListener('resize', () => { positionChatInput(); stabilizePositionDuringTransition(); });
        const mo = new MutationObserver(() => { positionChatInput(); stabilizePositionDuringTransition(); });
        mo.observe(document.body, { childList: true, subtree: true });

        setTimeout(() => { positionChatInput(); stabilizePositionDuringTransition(); }, 160);
    });

    (function () {
        let __panelsTO = null;
        function detectHeaderFooter() {
            const headerSelectors = ['header', '.navbar', '.topbar', '.site-header', '.main-header'];
            const footerSelectors = ['#nexusFooter', 'footer.site-footer', '.site-footer', '.nexus-notice'];
            function firstMatch(list) { for (const s of list) { const el = document.querySelector(s); if (el) return el; } return null; }
            const explicitFooter = firstMatch(footerSelectors);
            const explicitHeader = firstMatch(headerSelectors);

            const fixedBottomCandidates = [];
            Array.from(document.body.children).forEach(el => {
                try {
                    const cs = getComputedStyle(el);
                    if ((cs.position === 'fixed' || cs.position === 'sticky') && cs.display !== 'none') {
                        const rect = el.getBoundingClientRect();
                        if (rect.bottom >= window.innerHeight - 2 || rect.bottom >= window.innerHeight - 80) {
                            fixedBottomCandidates.push(el);
                        }
                    }
                } catch (e) { }
            });

            return { header: explicitHeader, footer: explicitFooter, fixedBottoms: fixedBottomCandidates };
        }

        function applyHeights() {
            try {
                const det = detectHeaderFooter();
                const headerH = det.header ? Math.ceil(det.header.getBoundingClientRect().height) : 0;

                let footerH = 0;
                if (det.footer) { try { footerH = Math.max(footerH, Math.ceil(det.footer.getBoundingClientRect().height)); } catch (e) { } }
                det.fixedBottoms.forEach(e => { try { footerH = Math.max(footerH, Math.ceil(e.getBoundingClientRect().height)); } catch (e) { } });

                const nexus = document.getElementById('nexusFooter');
                if (nexus) {
                    nexus.style.position = 'fixed';
                    nexus.style.left = '0';
                    nexus.style.right = '0';
                    nexus.style.bottom = '0';
                    nexus.style.zIndex = '1200';
                    nexus.style.width = '100%';
                    try { footerH = Math.max(footerH, Math.ceil(nexus.getBoundingClientRect().height)); } catch (e) { }
                }

                document.documentElement.style.setProperty('--footer-height', (footerH || 72) + 'px');
                document.body.style.paddingBottom = (footerH || 72) + 'px';

                const extra = 6;
                const available = Math.max(180, Math.floor(window.innerHeight - headerH - (footerH || 72) - extra));

                const container = document.querySelector('.app-container');
                if (container) container.style.minHeight = (window.innerHeight - headerH - (footerH || 72)) + 'px';

                qa('.col-lg-3, .col-lg-6, .col-lg-3').forEach(col => {
                    col.style.display = 'flex';
                    col.style.flexDirection = 'column';
                    col.style.paddingBottom = '0';
                    col.style.marginBottom = '0';
                    col.style.minHeight = '0';
                });

                qa('.card.card-panel-left, .card.card-panel-center, .card.card-panel-right').forEach(card => {
                    card.style.display = 'flex';
                    card.style.flexDirection = 'column';
                    card.style.height = available + 'px';
                    card.style.maxHeight = available + 'px';
                    card.style.minHeight = '0';
                    card.style.marginBottom = '0';
                });

                qa('.card.card-panel-center, .card.card-panel-right').forEach(card => {
                    const body = card.querySelector('.card-body.panel-body');
                    if (body) {
                        body.style.flex = '1 1 auto';
                        body.style.minHeight = '0';
                        const result = body.querySelector('.result-area');
                        if (result) {
                            result.style.flex = '1 1 auto';
                            result.style.minHeight = '0';
                            result.style.overflowY = 'auto';
                        }
                    }
                });

                qa('.card.card-panel-left .card-body.panel-body').forEach(body => {
                    body.style.flex = '1 1 auto';
                    body.style.minHeight = '0';
                    body.style.overflow = 'hidden';
                });

                const previewContent = document.querySelector('#previewContent');
                if (previewContent) {
                    const panel = previewContent.closest('.panel-height') || previewContent.closest('.card-body.panel-body');
                    if (panel) {
                        const panelRect = panel.getBoundingClientRect();
                        const previewHeader = panel.querySelector('.d-flex.align-items-center') || panel.querySelector('.card-header');
                        const headerInnerH = previewHeader ? Math.ceil(previewHeader.getBoundingClientRect().height) : 48;
                        const footerCompact = panel.querySelector('.mt-2.d-flex') || panel.querySelector('.card-footer');
                        const footerInnerH = footerCompact ? Math.ceil(footerCompact.getBoundingClientRect().height) : 40;
                        const innerAvailable = Math.max(120, Math.floor(panelRect.height - headerInnerH - footerInnerH - 16));
                        previewContent.style.height = innerAvailable + 'px';
                        previewContent.style.maxHeight = innerAvailable + 'px';
                        previewContent.style.minHeight = '0';
                        previewContent.style.overflowY = 'auto';
                        previewContent.style.boxSizing = 'border-box';
                    }
                }

                qa('.pdf-pages-container, .pdf-page-box').forEach(el => { el.style.overflow = 'visible'; });

                document.documentElement.style.setProperty('--available-panel-height', available + 'px');
            } catch (err) {
                console.warn('applyHeights error', err);
            }
        }

        function scheduleApply() {
            clearTimeout(__panelsTO);
            __panelsTO = setTimeout(applyHeights, 80);
        }

        window.addEventListener('load', scheduleApply);
        window.addEventListener('resize', scheduleApply);
        const mo = new MutationObserver(() => scheduleApply());
        mo.observe(document.body, { childList: true, subtree: true });

        window.__setPanelsHeight = applyHeights;
        scheduleApply();
    })();
})();


        (function () {
                // valores y elementos
                const panel = document.getElementById('cfTopPanel');
        if (!panel) return;
        const caseCode = panel.dataset.case || '';
        const buttons = Array.from(panel.querySelectorAll('.cf-toggle-btn'));
        const commentEl = document.getElementById('cfComment');
        const sendBtn = document.getElementById('cfSend');
        const cancelBtn = document.getElementById('cfCancel');
        const statusEl = document.getElementById('cfStatus');

        let state = {helped: null, comment: '' };

        function setStatus(text, tone) {
            statusEl.textContent = text || '';
        statusEl.className = 'small ' + (tone === 'ok' ? 'text-success' : tone === 'err' ? 'text-danger' : 'text-muted');
                }

        function clearSelection() {
            state.helped = null;
                    buttons.forEach(b => {b.classList.remove('active'); b.setAttribute('aria-pressed', 'false'); });
                }

                // toggle buttons estilo "tile"
                buttons.forEach(btn => {
            btn.addEventListener('click', () => {
                const val = btn.dataset.value === 'true';
                // si hizo click en mismo valor, toggle off
                if (state.helped === val) {
                    clearSelection();
                } else {
                    state.helped = val;
                    buttons.forEach(b => {
                        const isActive = (b === btn);
                        b.classList.toggle('active', isActive);
                        b.setAttribute('aria-pressed', isActive ? 'true' : 'false');
                    });
                }
                setStatus('', null);
            });
                });



                // enviar reseña
                sendBtn.addEventListener('click', async () => {
            state.comment = (commentEl && commentEl.value || '').trim();
        if (state.helped === null) {
            setStatus('Selecciona Sí/No o escribe un comentario antes de enviar.', 'err');
        return;
                    }


        // deshabilitar UI
        sendBtn.disabled = true;
                    buttons.forEach(b => b.disabled = true);
        setStatus('Enviando reseña…', 'muted');

        const payload = {caseCode: caseCode, helped: state.helped, comment: state.comment };

        try {
                        const resp = await fetch('@Url.Action("SubmitFeedback", "Nexus")', {
            method: 'POST',
        headers: {'Content-Type': 'application/json', 'Accept': 'application/json' },
        body: JSON.stringify(payload)
                        });

        if (!resp.ok) throw new Error('Status ' + resp.status);
        const data = await resp.json();
        if (data && data.success) {
            setStatus('Gracias — reseña enviada.', 'ok');
        sendBtn.classList.remove('btn-primary');
        sendBtn.classList.add('btn-success');
        sendBtn.textContent = 'Enviado';
                        } else {
                            throw new Error((data && data.message) ? data.message : 'No fue posible guardar la reseña.');
                        }
                    } catch (err) {
            console.error('Feedback error', err);
        // fallback local
        try {
                            const key = 'nexus_feedback_offline_' + (caseCode || 'unknown') + '_' + new Date().getTime();
        localStorage.setItem(key, JSON.stringify(Object.assign({ }, payload, {ts: new Date().toISOString() })));
        setStatus('No se pudo enviar ahora. Guardado localmente.', 'err');
                        } catch (e) {
            setStatus('Error al enviar reseña. Intenta nuevamente más tarde.', 'err');
                        }
        // restaurar estado de botón
        sendBtn.disabled = false;
                        buttons.forEach(b => b.disabled = false);
        cancelBtn.disabled = false;
        return;
                    }

        // mantener botón enviado inactivo
        sendBtn.disabled = true;
                    buttons.forEach(b => b.disabled = true);
        cancelBtn.disabled = true;
                });

                // accesibilidad: permitir teclado en botones
                buttons.forEach(b => {
            b.addEventListener('keydown', (ev) => {
                if (ev.key === 'Enter' || ev.key === ' ') {
                    ev.preventDefault();
                    b.click();
                }
            });
                });
            })();
            });