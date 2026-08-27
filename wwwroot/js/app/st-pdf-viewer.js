/* ============================================================================
   REQ-019 — Visor de documentos del Área Studio (Nexus IA)
   ----------------------------------------------------------------------------
   Visor PDF propio montado sobre PDF.js (wwwroot/lib/pdfjs), NO sobre el
   visor nativo del navegador.

   ¿Por qué no un <iframe>? Porque el visor nativo depende de la configuración
   del cliente ("descargar los PDF en lugar de abrirlos" lo desactiva), no
   permite saltar de forma fiable a una hoja concreta, y no deja pintar nada
   nuestro encima. Aquí se renderiza cada hoja a <canvas>, así el visor:

     · salta EXACTAMENTE a la hoja que traía el tag (que es el objetivo),
     · muestra los tags de esa hoja dentro de la propia barra del visor,
     · tiene miniaturas, zoom, ajuste al ancho, rotación y búsqueda de texto,
     · deja seleccionar y copiar texto (capa de texto de PDF.js), útil para
       copiar el número de factura o la cédula,
     · funciona igual en cualquier navegador y sin salir de la página.

   Uso:
       var v = StPdfViewer.create(document.getElementById('host'), {
           url:       '/Nexus/DownloadCaseDocument?...&inline=true',
           nombre:    'FACTURA.pdf',
           pagina:    2,
           etiquetas: { 2: [{ texto: 'PRO-FACTURA', clase: 'fam-pro' }] }
       });
       v.irAPagina(4);
   ========================================================================= */
(function (global) {
    'use strict';

    var WORKER_URL = '/lib/pdfjs/pdf.worker.min.js';

    // Cachés a nivel de página: reabrir el mismo documento es instantáneo.
    var cacheDocs = {};   // url -> Promise<PDFDocumentProxy>
    var cacheTexto = {};  // url -> { pagina: textoEnMinusculas }

    function lib() {
        var l = global.pdfjsLib;
        if (l && !lib._init) {
            l.GlobalWorkerOptions.workerSrc = WORKER_URL;
            lib._init = true;
        }
        return l;
    }

    function el(tag, cls, html) {
        var n = document.createElement(tag);
        if (cls) n.className = cls;
        if (html != null) n.innerHTML = html;
        return n;
    }

    function abrirDocumento(url) {
        if (!cacheDocs[url]) {
            cacheDocs[url] = lib().getDocument({ url: url, isEvalSupported: false }).promise
                .catch(function (e) { delete cacheDocs[url]; throw e; });
        }
        return cacheDocs[url];
    }

    // ── El visor ────────────────────────────────────────────────────────────
    function Visor(host, opts) {
        this.host = host;
        this.opts = opts || {};
        this.url = null;
        this.nombre = '';
        this.doc = null;
        this.pagina = 1;
        this.total = 1;
        this.zoom = 1;
        this.ajustarAncho = true;
        this.rotacion = 0;
        this.etiquetas = {};
        this.tareaRender = null;
        this.miniaturasHechas = false;
        this._construir();
    }

    Visor.prototype._construir = function () {
        var self = this;
        this.host.innerHTML = '';
        this.host.classList.add('stpdf');

        // ── Barra de herramientas ──
        var bar = el('div', 'stpdf-bar');

        var navG = el('span', 'stpdf-grp');
        this.bPrev = el('button', 'stpdf-btn', '<i class="fa fa-chevron-left"></i>');
        this.bPrev.title = 'Hoja anterior';
        this.inPag = el('input', 'stpdf-pag');
        this.inPag.type = 'text';
        this.lblTotal = el('span', 'stpdf-total', '/ 1');
        this.bNext = el('button', 'stpdf-btn', '<i class="fa fa-chevron-right"></i>');
        this.bNext.title = 'Hoja siguiente';
        navG.appendChild(this.bPrev);
        navG.appendChild(this.inPag);
        navG.appendChild(this.lblTotal);
        navG.appendChild(this.bNext);

        var zoomG = el('span', 'stpdf-grp');
        this.bMenos = el('button', 'stpdf-btn', '<i class="fa fa-search-minus"></i>');
        this.bMenos.title = 'Reducir';
        this.lblZoom = el('span', 'stpdf-zoom', '—');
        this.bMas = el('button', 'stpdf-btn', '<i class="fa fa-search-plus"></i>');
        this.bMas.title = 'Ampliar';
        this.bFit = el('button', 'stpdf-btn', '<i class="fa fa-arrows-h"></i>');
        this.bFit.title = 'Ajustar al ancho';
        this.bRot = el('button', 'stpdf-btn', '<i class="fa fa-repeat"></i>');
        this.bRot.title = 'Girar 90 grados';
        [this.bMenos, this.lblZoom, this.bMas, this.bFit, this.bRot]
            .forEach(function (n) { zoomG.appendChild(n); });

        var busG = el('span', 'stpdf-grp stpdf-grp-buscar');
        this.inBuscar = el('input', 'stpdf-buscar');
        this.inBuscar.type = 'search';
        this.inBuscar.placeholder = 'Buscar en el documento...';
        this.bBuscar = el('button', 'stpdf-btn', '<i class="fa fa-search"></i>');
        this.bBuscar.title = 'Buscar';
        this.lblBuscar = el('span', 'stpdf-hits');
        busG.appendChild(this.inBuscar);
        busG.appendChild(this.bBuscar);
        busG.appendChild(this.lblBuscar);

        bar.appendChild(navG);
        bar.appendChild(zoomG);
        bar.appendChild(busG);

        // ── Tags de la hoja visible: el "por qué" de estar en esta página ──
        this.barTags = el('div', 'stpdf-tags');

        // ── Cuerpo: miniaturas + lienzo ──
        var cuerpo = el('div', 'stpdf-body');
        this.minis = el('div', 'stpdf-minis');
        this.lienzoWrap = el('div', 'stpdf-scroll');
        this.pagWrap = el('div', 'stpdf-page');
        this.canvas = el('canvas');
        this.capaTexto = el('div', 'textLayer');
        this.pagWrap.appendChild(this.canvas);
        this.pagWrap.appendChild(this.capaTexto);
        this.lienzoWrap.appendChild(this.pagWrap);
        cuerpo.appendChild(this.minis);
        cuerpo.appendChild(this.lienzoWrap);

        this.aviso = el('div', 'stpdf-aviso');
        this.aviso.style.display = 'none';

        this.host.appendChild(bar);
        this.host.appendChild(this.barTags);
        this.host.appendChild(this.aviso);
        this.host.appendChild(cuerpo);

        // ── Eventos ──
        this.bPrev.onclick = function () { self.irAPagina(self.pagina - 1); };
        this.bNext.onclick = function () { self.irAPagina(self.pagina + 1); };
        this.inPag.onchange = function () { self.irAPagina(parseInt(self.inPag.value, 10)); };
        this.bMenos.onclick = function () { self._zoom(self._escalaActual() / 1.25); };
        this.bMas.onclick = function () { self._zoom(self._escalaActual() * 1.25); };
        this.bFit.onclick = function () { self.ajustarAncho = true; self._render(); };
        this.bRot.onclick = function () { self.rotacion = (self.rotacion + 90) % 360; self._render(); };
        this.bBuscar.onclick = function () { self.buscar(self.inBuscar.value); };
        this.inBuscar.onkeydown = function (e) {
            if (e.key === 'Enter') { e.preventDefault(); self.buscar(self.inBuscar.value); }
        };

        // Teclado: flechas para hojear, +/- para zoom (con el visor enfocado)
        this.host.tabIndex = 0;
        this.host.addEventListener('keydown', function (e) {
            if (e.target === self.inBuscar || e.target === self.inPag) return;
            if (e.key === 'ArrowRight' || e.key === 'PageDown') { self.irAPagina(self.pagina + 1); e.preventDefault(); }
            else if (e.key === 'ArrowLeft' || e.key === 'PageUp') { self.irAPagina(self.pagina - 1); e.preventDefault(); }
            else if (e.key === '+') { self._zoom(self._escalaActual() * 1.25); e.preventDefault(); }
            else if (e.key === '-') { self._zoom(self._escalaActual() / 1.25); e.preventDefault(); }
        });

        // Al cambiar el ancho disponible, re-ajustar (sin repintar en cada píxel)
        var t = null;
        window.addEventListener('resize', function () {
            if (!self.doc || !self.ajustarAncho) return;
            clearTimeout(t);
            t = setTimeout(function () { self._render(); }, 180);
        });
    };

    Visor.prototype._escalaActual = function () { return this.zoom || 1; };

    Visor.prototype._zoom = function (z) {
        this.ajustarAncho = false;
        this.zoom = Math.min(6, Math.max(0.2, z));
        this._render();
    };

    Visor.prototype._msg = function (txt, esError) {
        this.aviso.style.display = txt ? 'block' : 'none';
        this.aviso.className = 'stpdf-aviso' + (esError ? ' is-error' : '');
        this.aviso.textContent = txt || '';
    };

    /** Abre un documento y salta a una hoja concreta. */
    Visor.prototype.abrir = function (o) {
        var self = this;
        if (!lib()) {
            this._msg('No se pudo cargar PDF.js (revisa /lib/pdfjs).', true);
            return Promise.resolve();
        }

        this.url = o.url;
        this.nombre = o.nombre || '';
        this.etiquetas = o.etiquetas || {};
        this.evidencia = o.evidencia || null;   // frase a resaltar dentro de la hoja
        this.rotacion = 0;
        this.ajustarAncho = true;
        this.miniaturasHechas = false;
        this.minis.innerHTML = '';
        this.lblBuscar.innerHTML = '';
        var destino = o.pagina || 1;

        // No es PDF (imagen escaneada suelta): se muestra tal cual
        if (this.nombre && !/\.pdf$/i.test(this.nombre)) {
            this.doc = null;
            this.total = 1;
            this.pagina = 1;
            this._pintarBarra();
            this.capaTexto.innerHTML = '';
            this.lienzoWrap.innerHTML = '<img class="stpdf-img" src="' + o.url + '" alt="" />';
            this._msg('');
            return Promise.resolve();
        }

        this._msg('Cargando documento...');
        return abrirDocumento(o.url).then(function (doc) {
            self.doc = doc;
            self.total = doc.numPages;
            self.pagina = Math.min(Math.max(1, destino), self.total);
            self._msg('');
            // Reponer el lienzo si antes se mostró una imagen
            if (!self.lienzoWrap.contains(self.pagWrap)) {
                self.lienzoWrap.innerHTML = '';
                self.lienzoWrap.appendChild(self.pagWrap);
            }
            return self._render().then(function () {
                self._miniaturas();
                if (self.evidencia) self.resaltar(self.evidencia);
            });
        }).catch(function (e) {
            self._msg('No se pudo abrir el documento: ' + (e && e.message ? e.message : e), true);
        });
    };

    Visor.prototype.irAPagina = function (n, evidencia) {
        if (!this.doc) return Promise.resolve();
        var self = this;
        n = parseInt(n, 10);
        if (isNaN(n)) { this.inPag.value = this.pagina; return Promise.resolve(); }
        n = Math.min(Math.max(1, n), this.total);

        if (n === this.pagina) {
            this.inPag.value = n;
            return evidencia ? this.resaltar(evidencia) : Promise.resolve();
        }
        this.pagina = n;
        return this._render().then(function () {
            if (evidencia) return self.resaltar(evidencia);
        });
    };

    Visor.prototype._pintarBarra = function () {
        this.inPag.value = this.pagina;
        this.lblTotal.textContent = '/ ' + this.total;
        this.bPrev.disabled = this.pagina <= 1;
        this.bNext.disabled = this.pagina >= this.total;
        this.lblZoom.textContent = Math.round(this._escalaActual() * 100) + '%';

        // Tags de esta hoja (los que produjo la tipificación)
        var tags = this.etiquetas[this.pagina] || [];
        this.barTags.innerHTML = '';
        if (!tags.length) { this.barTags.style.display = 'none'; return; }
        this.barTags.style.display = 'flex';
        this.barTags.appendChild(el('span', 'stpdf-tags-lbl', 'Tags de esta hoja:'));
        tags.forEach(function (t) {
            this.barTags.appendChild(
                el('span', 'st-tipo-chip ' + (t.clase || 'fam-general'), t.texto));
        }, this);
    };

    Visor.prototype._render = function () {
        var self = this;
        if (!this.doc) { this._pintarBarra(); return Promise.resolve(); }

        // Cancelar el pintado anterior: hojear rápido no debe encolar trabajo
        if (this.tareaRender) {
            try { this.tareaRender.cancel(); } catch (e) { }
            this.tareaRender = null;
        }

        return this.doc.getPage(this.pagina).then(function (page) {
            var base = page.getViewport({ scale: 1, rotation: self.rotacion });
            if (self.ajustarAncho) {
                var disp = self.lienzoWrap.clientWidth || 800;
                self.zoom = Math.max(0.2, (disp - 28) / base.width);
            }
            var vp = page.getViewport({ scale: self.zoom, rotation: self.rotacion });
            var dpr = global.devicePixelRatio || 1;

            self.canvas.width = Math.floor(vp.width * dpr);
            self.canvas.height = Math.floor(vp.height * dpr);
            self.canvas.style.width = Math.floor(vp.width) + 'px';
            self.canvas.style.height = Math.floor(vp.height) + 'px';
            self.pagWrap.style.width = Math.floor(vp.width) + 'px';
            self.pagWrap.style.height = Math.floor(vp.height) + 'px';

            var ctx = self.canvas.getContext('2d');
            ctx.setTransform(1, 0, 0, 1, 0, 0);
            ctx.clearRect(0, 0, self.canvas.width, self.canvas.height);

            self.tareaRender = page.render({
                canvasContext: ctx,
                viewport: vp,
                transform: dpr !== 1 ? [dpr, 0, 0, dpr, 0, 0] : null
            });

            self._pintarBarra();
            self._marcarMini();

            return self.tareaRender.promise.then(function () {
                self.tareaRender = null;
                return self._capaDeTexto(page, vp);
            });
        }).catch(function (e) {
            if (e && e.name === 'RenderingCancelledException') return;  // hojeo rápido: normal
            self._msg('Error al pintar la hoja: ' + (e && e.message ? e.message : e), true);
        });
    };

    /** Capa de texto: permite seleccionar y copiar (nº de factura, cédula...). */
    Visor.prototype._capaDeTexto = function (page, vp) {
        var self = this;
        this.capaTexto.innerHTML = '';
        this.capaTexto.style.width = Math.floor(vp.width) + 'px';
        this.capaTexto.style.height = Math.floor(vp.height) + 'px';
        // PDF.js 3.x posiciona la capa con esta variable CSS: sin ella el texto
        // sale descolocado respecto al lienzo.
        this.capaTexto.style.setProperty('--scale-factor', vp.scale);

        return page.getTextContent().then(function (tc) {
            var t = lib().renderTextLayer({
                textContentSource: tc,
                textContent: tc,
                container: self.capaTexto,
                viewport: vp
            });
            return t && t.promise ? t.promise : t;
        }).catch(function () { /* la capa de texto es un extra: nunca rompe el visor */ });
    };

    // ── Evidencia: localizar, resaltar y recortar un trozo de la hoja ───────
    //
    // El OCR que guardamos no trae coordenadas, pero la capa de texto de PDF.js
    // sí: cada fragmento viene con su transform (x, y) y su ancho. Con eso se
    // localiza la frase en la hoja y se puede (a) marcarla como con un
    // resaltador y (b) recortar esa región para mostrarla como miniatura.

    /** Texto comparable: sin tildes, mayúsculas, sin puntuación y sin espacios de más. */
    function normalizar(t) {
        return (t || '')
            .normalize('NFD').replace(/[\u0300-\u036f]/g, '')
            .toUpperCase()
            .replace(/[^A-Z0-9]+/g, ' ')
            .trim();
    }

    /**
     * Busca una frase dentro de una hoja y devuelve su recuadro en unidades de
     * PDF: { x, y, w, h }. Devuelve null si no la encuentra — y eso es una
     * respuesta válida: el OCR y el texto embebido del PDF no siempre coinciden
     * (documentos escaneados sin capa de texto, por ejemplo).
     */
    Visor.prototype.localizarEnPagina = function (numPagina, texto) {
        var self = this;
        var aguja = normalizar(texto);
        if (!this.doc || aguja.length < 3) return Promise.resolve(null);

        return this.doc.getPage(numPagina).then(function (page) {
            return page.getTextContent().then(function (tc) {
                // Cadena normalizada de toda la hoja, guardando de qué item sale cada tramo
                var pajar = '', mapa = [];
                tc.items.forEach(function (it, idx) {
                    var trozo = normalizar(it.str);
                    if (!trozo) return;
                    if (pajar) pajar += ' ';
                    mapa.push({ ini: pajar.length, fin: pajar.length + trozo.length, idx: idx });
                    pajar += trozo;
                });

                var pos = pajar.indexOf(aguja);
                if (pos === -1) {
                    // Segundo intento: las 4 primeras palabras (el OCR suele cortar el final)
                    var corto = aguja.split(' ').slice(0, 4).join(' ');
                    if (corto.length < 6) return null;
                    pos = pajar.indexOf(corto);
                    if (pos === -1) return null;
                    aguja = corto;
                }
                var fin = pos + aguja.length;

                // Unión de los recuadros de todos los items tocados
                var caja = null;
                mapa.forEach(function (m) {
                    if (m.fin <= pos || m.ini >= fin) return;
                    var it = tc.items[m.idx];
                    var t = it.transform;                       // [a,b,c,d,e,f]
                    var x = t[4], y = t[5];                     // esquina inferior izquierda
                    var w = it.width || 0, h = it.height || Math.abs(t[3]) || 10;
                    var r = { x0: x, y0: y, x1: x + w, y1: y + h };
                    if (!caja) caja = r;
                    else {
                        caja.x0 = Math.min(caja.x0, r.x0); caja.y0 = Math.min(caja.y0, r.y0);
                        caja.x1 = Math.max(caja.x1, r.x1); caja.y1 = Math.max(caja.y1, r.y1);
                    }
                });
                if (!caja) return null;

                var margen = 3;
                return {
                    x: caja.x0 - margen,
                    y: caja.y0 - margen,
                    w: (caja.x1 - caja.x0) + margen * 2,
                    h: (caja.y1 - caja.y0) + margen * 2,
                    pagina: numPagina
                };
            });
        }).catch(function () { return null; });
    };

    /** Marca la frase en la hoja visible y hace scroll hasta ella. */
    Visor.prototype.resaltar = function (texto) {
        var self = this;
        this.limpiarMarcas();
        return this.localizarEnPagina(this.pagina, texto).then(function (bbox) {
            if (!bbox) {
                self._avisoEvidencia('No se pudo ubicar el fragmento en esta hoja ' +
                                     '(el documento puede no tener capa de texto).');
                return null;
            }
            return self.doc.getPage(self.pagina).then(function (page) {
                var vp = page.getViewport({ scale: self.zoom, rotation: self.rotacion });
                // El PDF tiene el origen abajo-izquierda; el viewport lo convierte
                var a = vp.convertToViewportPoint(bbox.x, bbox.y);
                var b = vp.convertToViewportPoint(bbox.x + bbox.w, bbox.y + bbox.h);

                var marca = el('div', 'stpdf-marca');
                marca.style.left = Math.min(a[0], b[0]) + 'px';
                marca.style.top = Math.min(a[1], b[1]) + 'px';
                marca.style.width = Math.abs(b[0] - a[0]) + 'px';
                marca.style.height = Math.abs(b[1] - a[1]) + 'px';
                self.pagWrap.appendChild(marca);

                // Centrar la marca en el scroll del visor
                var cont = self.lienzoWrap;
                cont.scrollTop = Math.max(0, marca.offsetTop - cont.clientHeight / 2 + marca.offsetHeight / 2);
                cont.scrollLeft = Math.max(0, marca.offsetLeft - cont.clientWidth / 2 + marca.offsetWidth / 2);

                self._avisoEvidencia('');
                return bbox;
            });
        });
    };

    Visor.prototype.limpiarMarcas = function () {
        var viejas = this.pagWrap.querySelectorAll('.stpdf-marca');
        Array.prototype.forEach.call(viejas, function (m) { m.parentNode.removeChild(m); });
    };

    Visor.prototype._avisoEvidencia = function (txt) {
        if (!this._avisoEv) {
            this._avisoEv = el('div', 'stpdf-aviso-ev');
            this.barTags.parentNode.insertBefore(this._avisoEv, this.barTags.nextSibling);
        }
        this._avisoEv.textContent = txt || '';
        this._avisoEv.style.display = txt ? 'block' : 'none';
    };

    /**
     * Recorta la región de la hoja y la devuelve como dataURL: es la miniatura
     * del trozo del documento, para verlo sin abrir el visor entero.
     */
    Visor.prototype.recorteDataUrl = function (numPagina, bbox, anchoDestino) {
        var self = this;
        if (!this.doc || !bbox) return Promise.resolve(null);
        return this.doc.getPage(numPagina).then(function (page) {
            var escala = 2.2;                       // suficiente para que se lea
            var vp = page.getViewport({ scale: escala });
            var lienzo = document.createElement('canvas');
            lienzo.width = Math.ceil(vp.width);
            lienzo.height = Math.ceil(vp.height);
            // Mismo tope que en la portada: un render que no termina no puede
            // dejar el aviso de "buscando…" colgado para siempre. pdf.js dibuja
            // apoyandose en requestAnimationFrame y, si la pestana no esta
            // componiendo frames, la promesa nunca se resuelve.
            var tareaRec = page.render({ canvasContext: lienzo.getContext('2d'), viewport: vp });
            return Promise.race([
                tareaRec.promise,
                new Promise(function (r) {
                    setTimeout(function () {
                        try { tareaRec.cancel(); } catch (e) { }
                        r('__sin_render__');
                    }, 7000);
                })
            ])
                .then(function (fin) {
                    if (fin === '__sin_render__') return null;
                    var a = vp.convertToViewportPoint(bbox.x, bbox.y);
                    var b = vp.convertToViewportPoint(bbox.x + bbox.w, bbox.y + bbox.h);
                    // Un poco de aire alrededor: el contexto ayuda a entender el recorte
                    var aire = 14 * escala;
                    var x = Math.max(0, Math.min(a[0], b[0]) - aire);
                    var y = Math.max(0, Math.min(a[1], b[1]) - aire);
                    var w = Math.min(lienzo.width - x, Math.abs(b[0] - a[0]) + aire * 2);
                    var h = Math.min(lienzo.height - y, Math.abs(b[1] - a[1]) + aire * 2);
                    if (w < 4 || h < 4) return null;

                    var destino = document.createElement('canvas');
                    var factor = (anchoDestino || 420) / w;
                    destino.width = Math.round(w * factor);
                    destino.height = Math.round(h * factor);
                    var ctx = destino.getContext('2d');
                    ctx.drawImage(lienzo, x, y, w, h, 0, 0, destino.width, destino.height);

                    // Marca del resaltador sobre el recorte, para que se vea QUÉ es la evidencia
                    ctx.fillStyle = 'rgba(255, 214, 0, 0.28)';
                    ctx.strokeStyle = 'rgba(214, 158, 0, 0.9)';
                    ctx.lineWidth = 2;
                    var mx = (Math.min(a[0], b[0]) - x) * factor;
                    var my = (Math.min(a[1], b[1]) - y) * factor;
                    var mw = Math.abs(b[0] - a[0]) * factor;
                    var mh = Math.abs(b[1] - a[1]) * factor;
                    ctx.fillRect(mx, my, mw, mh);
                    ctx.strokeRect(mx, my, mw, mh);

                    return destino.toDataURL('image/png');
                });
        }).catch(function () { return null; });
    };

    // ── Miniaturas ──────────────────────────────────────────────────────────
    Visor.prototype._miniaturas = function () {
        var self = this;
        if (!this.doc || this.miniaturasHechas) return;
        this.miniaturasHechas = true;
        this.minis.innerHTML = '';

        var i = 1;
        // En serie (no en paralelo): un sobre de 20 hojas no debe pelearse por CPU
        // con el pintado de la hoja principal.
        function siguiente() {
            if (i > self.total) return;
            var n = i++;
            var celda = el('div', 'stpdf-mini');
            celda.dataset.pag = n;
            var c = el('canvas');
            celda.appendChild(c);
            celda.appendChild(el('span', 'stpdf-mini-num', String(n)));
            if ((self.etiquetas[n] || []).length) celda.appendChild(el('span', 'stpdf-mini-dot'));
            celda.onclick = function () { self.irAPagina(n); };
            self.minis.appendChild(celda);

            self.doc.getPage(n).then(function (page) {
                var v0 = page.getViewport({ scale: 1 });
                var vp = page.getViewport({ scale: 104 / v0.width });
                c.width = Math.floor(vp.width);
                c.height = Math.floor(vp.height);
                return page.render({ canvasContext: c.getContext('2d'), viewport: vp }).promise;
            }).then(function () { self._marcarMini(); siguiente(); })
                .catch(function () { siguiente(); });
        }
        siguiente();
    };

    Visor.prototype._marcarMini = function () {
        var act = this.minis.querySelector('.stpdf-mini.is-act');
        if (act) act.classList.remove('is-act');
        var n = this.minis.querySelector('.stpdf-mini[data-pag="' + this.pagina + '"]');
        if (n) {
            n.classList.add('is-act');
            if (n.scrollIntoView) n.scrollIntoView({ block: 'nearest' });
        }
    };

    // ── Búsqueda en todo el documento ───────────────────────────────────────
    Visor.prototype._textoDe = function (n) {
        var url = this.url;
        cacheTexto[url] = cacheTexto[url] || {};
        if (cacheTexto[url][n] != null) return Promise.resolve(cacheTexto[url][n]);
        return this.doc.getPage(n)
            .then(function (p) { return p.getTextContent(); })
            .then(function (tc) {
                var t = tc.items.map(function (x) { return x.str; }).join(' ').toLowerCase();
                cacheTexto[url][n] = t;
                return t;
            });
    };

    Visor.prototype.buscar = function (q) {
        var self = this;
        q = (q || '').trim().toLowerCase();
        this.lblBuscar.innerHTML = '';
        if (!this.doc || q.length < 2) return Promise.resolve();

        this.lblBuscar.textContent = 'buscando...';
        var pend = [];
        for (var n = 1; n <= this.total; n++) pend.push(n);

        return Promise.all(pend.map(function (n) {
            return self._textoDe(n).then(function (t) {
                var c = 0, i = t.indexOf(q);
                while (i !== -1) { c++; i = t.indexOf(q, i + q.length); }
                return { pagina: n, veces: c };
            }).catch(function () { return { pagina: n, veces: 0 }; });
        })).then(function (res) {
            var hits = res.filter(function (r) { return r.veces > 0; });
            var tot = hits.reduce(function (a, r) { return a + r.veces; }, 0);
            self.lblBuscar.innerHTML = '';
            if (!hits.length) { self.lblBuscar.textContent = 'sin coincidencias'; return; }
            self.lblBuscar.appendChild(
                el('span', null, tot + (tot === 1 ? ' coincidencia en ' : ' coincidencias en ')));
            hits.forEach(function (h) {
                var b = el('button', 'stpdf-hit',
                    'pag. ' + h.pagina + (h.veces > 1 ? ' (' + h.veces + ')' : ''));
                b.onclick = function () { self.irAPagina(h.pagina); };
                self.lblBuscar.appendChild(b);
            });
            self.irAPagina(hits[0].pagina);
        });
    };

    /**
     * Miniatura de un trozo de documento SIN abrir el visor: carga el PDF (usa
     * la misma caché), localiza la frase y devuelve el recorte como dataURL.
     * Es lo que se muestra al pulsar "segmento" en una fila de evidencia.
     */
    /**
     * Las formas en que el mismo dato puede estar escrito en el papel.
     *
     * Hace falta porque lo que guardamos y lo que imprime el prestador no
     * coinciden. El caso medido: el CIE10 se normaliza sin puntos (F900) y el
     * documento escribe F90.0, asi que la busqueda literal no encontraba nada
     * aunque el texto estuviera delante.
     */
    function variantes(texto) {
        var t = String(texto || '').trim();
        if (!t) return [];
        var v = [t];

        // CIE10 sin puntos -> con punto: los tres primeros caracteres son la
        // categoria y el resto va detras del punto (F900 -> F90.0).
        var cie = /^([A-Za-z]\d{2})(\d{1,2})$/.exec(t);
        if (cie) v.push(cie[1] + '.' + cie[2]);

        // Y al reves, por si el dato viniera con punto y el papel sin el.
        if (t.indexOf('.') >= 0) v.push(t.replace(/\./g, ''));

        // Numeros de factura y RUC a veces llevan separadores distintos.
        if (/[-\s]/.test(t)) v.push(t.replace(/[-\s]/g, ''));

        return v;
    }

    function miniaturaDeEvidencia(url, numPagina, texto, anchoDestino) {
        if (!lib()) return Promise.resolve(null);
        return abrirDocumento(url).then(function (doc) {
            // Visor "de bolsillo": solo para reutilizar localizar y recortar
            var v = Object.create(Visor.prototype);
            v.doc = doc; v.zoom = 1; v.rotacion = 0;

            // Sin página conocida se recorre el documento. Es el caso de los
            // diagnósticos: se agrupan por código y el dato viene del documento
            // entero, no de una hoja concreta.
            // Se prueban todas las formas posibles del dato antes de rendirse.
            var formas = variantes(texto);
            function porFormas(i) {
                if (i >= formas.length) return Promise.resolve(null);
                var intento = numPagina
                    ? v.localizarEnPagina(numPagina, formas[i]).then(function (bb) {
                          return bb ? { pagina: numPagina, bbox: bb } : null;
                      })
                    : localizarEnDocumento(v, formas[i]);
                return intento.then(function (h) { return h || porFormas(i + 1); });
            }
            var buscar = porFormas(0);

            return buscar.then(function (hallazgo) {
                if (hallazgo) {
                    return v.recorteDataUrl(hallazgo.pagina, hallazgo.bbox, anchoDestino)
                        .then(function (d) { return d ? { img: d, exacto: true } : null; });
                }

                // Sin capa de texto no hay forma de señalar el punto: el OCR
                // vive en nuestra base, no dentro del fichero. Pero SÍ se puede
                // enseñar la hoja, que sigue siendo mucho mejor que un "no
                // pudimos" — el afiliado la mira y lo encuentra él.
                return v.paginaCompletaDataUrl(numPagina || 1, anchoDestino)
                    .then(function (d) { return d ? { img: d, exacto: false } : null; });
            });
        }).catch(function () { return null; });
    }

    /**
     * Recorre las páginas hasta encontrar la frase. Se va de una en una y se
     * para en la primera coincidencia: cargar el texto de todas las hojas de un
     * sobre grande a la vez no compensa cuando lo normal es acertar en las
     * primeras.
     */
    function localizarEnDocumento(v, texto) {
        var total = v.doc.numPages;
        var i = 0;
        function siguiente() {
            i++;
            if (i > total) return Promise.resolve(null);
            return v.localizarEnPagina(i, texto).then(function (bbox) {
                return bbox ? { pagina: i, bbox: bbox } : siguiente();
            });
        }
        return siguiente();
    }

    /**
     * La hoja entera, para cuando no se puede señalar el punto exacto: pasa con
     * las fotos y los escaneados, que no llevan capa de texto dentro del PDF.
     */
    Visor.prototype.paginaCompletaDataUrl = function (numPagina, anchoDestino) {
        if (!this.doc) return Promise.resolve(null);
        var n = Math.max(1, Math.min(numPagina || 1, this.doc.numPages));
        return this.doc.getPage(n).then(function (page) {
            var base = page.getViewport({ scale: 1 });
            var escala = (anchoDestino || 420) / base.width;
            var vp = page.getViewport({ scale: escala });
            var lienzo = document.createElement('canvas');
            lienzo.width = Math.max(1, Math.ceil(vp.width));
            lienzo.height = Math.max(1, Math.ceil(vp.height));
            var ctx = lienzo.getContext('2d');
            ctx.fillStyle = '#fff';
            ctx.fillRect(0, 0, lienzo.width, lienzo.height);
            var tarea = page.render({ canvasContext: ctx, viewport: vp });
            return Promise.race([
                tarea.promise.then(function () { return lienzo.toDataURL('image/jpeg', 0.75); }),
                new Promise(function (r) {
                    setTimeout(function () {
                        try { tarea.cancel(); } catch (e) { }
                        r(null);
                    }, 6000);
                })
            ]);
        }).catch(function () { return null; });
    };

    /** En qué página aparece la frase; null si no está en ninguna. */
    function paginaDeEvidencia(url, texto) {
        if (!lib()) return Promise.resolve(null);
        return abrirDocumento(url).then(function (doc) {
            var v = Object.create(Visor.prototype);
            v.doc = doc; v.zoom = 1; v.rotacion = 0;
            return localizarEnDocumento(v, texto).then(function (h) {
                return h ? h.pagina : null;
            });
        }).catch(function () { return null; });
    }

    /**
     * Portada del documento: la primera hoja, en pequeño, para que el afiliado
     * RECONOZCA de un vistazo cuál de sus papeles es cada tarjeta. Un nombre de
     * fichero como "NA-2612602-Costa-IND-549616-2.pdf" no le dice nada; ver la
     * factura, sí.
     *
     * Se cachea en memoria por url: la misma portada se pide cada vez que la
     * pantalla se repinta y renderizar un PDF no es barato.
     */
    var cachePortadas = {};

    function portadaDataUrl(url, anchoDestino) {
        if (!lib()) return Promise.resolve(null);
        if (cachePortadas[url]) return cachePortadas[url];

        var ancho = anchoDestino || 150;
        cachePortadas[url] = abrirDocumento(url).then(function (doc) {
            return doc.getPage(1).then(function (page) {
                var base = page.getViewport({ scale: 1 });
                var escala = ancho / base.width;
                var vp = page.getViewport({ scale: escala });

                var lienzo = document.createElement('canvas');
                lienzo.width = Math.max(1, Math.floor(vp.width));
                lienzo.height = Math.max(1, Math.floor(vp.height));
                var ctx = lienzo.getContext('2d');

                // Fondo blanco: un PDF sin fondo sale transparente y en la
                // tarjeta se vería como un recuadro roto.
                ctx.fillStyle = '#fff';
                ctx.fillRect(0, 0, lienzo.width, lienzo.height);

                // Un render que no termina no puede bloquear al resto. Pasa de
                // verdad: pdf.js dibuja apoyandose en requestAnimationFrame, y
                // si la pestana no esta componiendo frames (minimizada, en
                // segundo plano, o un panel oculto) la promesa NUNCA se
                // resuelve. Sin este tope, un solo documento atascado deja la
                // lista entera sin portadas.
                var tarea = page.render({ canvasContext: ctx, viewport: vp });
                return Promise.race([
                    tarea.promise.then(function () {
                        return lienzo.toDataURL('image/jpeg', 0.72);
                    }),
                    new Promise(function (resolve) {
                        setTimeout(function () {
                            try { tarea.cancel(); } catch (e) { /* ya terminaba */ }
                            resolve(null);
                        }, 6000);
                    })
                ]);
            });
        }).catch(function () {
            // Una foto que pdf.js no sabe abrir (HEIC, por ejemplo) no tiene
            // portada, y eso no es un error: simplemente no se pinta.
            delete cachePortadas[url];
            return null;
        });

        return cachePortadas[url];
    }

    global.StPdfViewer = {
        create: function (host, opts) {
            var v = new Visor(host, opts);
            if (opts && opts.url) v.abrir(opts);
            return v;
        },
        miniatura: miniaturaDeEvidencia,
        portada: portadaDataUrl,
        paginaDe: paginaDeEvidencia
    };
})(window);
