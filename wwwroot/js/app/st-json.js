/* ============================================================================
   REQ-019 — Ningún JSON crudo en pantalla
   ----------------------------------------------------------------------------
   Los agentes responden JSON porque su salida se persiste y se vuelve a leer.
   Volcarlo tal cual en la interfaz es ilegible: el operador ve llaves y
   corchetes donde debería ver el dato.

   Este renderizador convierte cualquier bloque JSON en fichas legibles y deja
   el original plegado por si el analista lo necesita. Se usa en:
     · el chat (respuestas de los agentes estructurados)
     · las notas del caso (ContextoSobre, ResolucionReembolso, Expediente…)
     · los fallbacks "control humano" de Resolución y Auditoría

   Uso:
     StJson.aplicar(nodo)            // re-pinta el contenido del nodo
     StJson.aplicarTodos('.selector')
     StJson.pintar(objeto)           // devuelve el HTML
   ========================================================================= */
(function (global) {
    'use strict';

    /** Nombres de campo conocidos: se muestran con la etiqueta del negocio. */
    var ETIQUETAS = {
        numeroSobre: 'Número de sobre',
        numeroContrato: 'Contrato',
        codigoProducto: 'Producto',
        codigoRegion: 'Región',
        nombreTitular: 'Titular',
        numeroPersonaPaciente: 'Nº de persona',
        origen: 'Origen',
        cedula: 'Cédula',
        fichaCliente: 'Ficha del cliente',
        resumenCaso: 'Resumen del caso',
        tipoAtencion: 'Tipo de atención',
        valorTotal: 'Valor total',
        numeroFactura: 'Número de factura'
    };

    function escapar(s) {
        return String(s).replace(/[<>&]/g, function (c) {
            return { '<': '&lt;', '>': '&gt;', '&': '&amp;' }[c];
        });
    }

    function etiqueta(k) {
        if (ETIQUETAS[k]) return ETIQUETAS[k];
        return String(k)
            .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
            .replace(/[_-]+/g, ' ')
            .replace(/^./, function (c) { return c.toUpperCase(); });
    }

    function valor(v) {
        if (v === null || v === undefined || v === '') return '<span class="st-json-nulo">—</span>';
        if (typeof v === 'boolean') {
            return v ? '<span class="st-json-si"><i class="fa fa-check"></i> sí</span>'
                     : '<span class="st-json-no"><i class="fa fa-times"></i> no</span>';
        }
        if (typeof v === 'number') return '<span class="st-json-num">' + v + '</span>';
        return escapar(v);
    }

    /**
     * Extrae el JSON de un texto. Tolera que venga envuelto en una valla de
     * código o con texto alrededor (los modelos a veces añaden un preámbulo).
     */
    function extraer(txt) {
        if (!txt) return null;
        var t = String(txt).trim();

        var valla = t.match(/^```(?:json)?\s*([\s\S]*?)```$/);
        if (valla) t = valla[1].trim();

        if (t.charAt(0) === '{' || t.charAt(0) === '[') {
            try { return JSON.parse(t); } catch (e) { /* sigue abajo */ }
        }

        // Último intento: el primer objeto o array bien formado dentro del texto
        var ini = t.search(/[{[]/);
        if (ini < 0) return null;
        var fin = Math.max(t.lastIndexOf('}'), t.lastIndexOf(']'));
        if (fin <= ini) return null;
        try { return JSON.parse(t.slice(ini, fin + 1)); } catch (e) { return null; }
    }

    /**
     * Pinta el dato como fichas. La profundidad está acotada: pasada cierta
     * anidación se muestra el crudo, que es más honesto que una ficha ilegible.
     */
    function pintar(dato, nivel) {
        nivel = nivel || 0;
        if (nivel > 3) {
            return '<pre class="st-json-crudo">' + escapar(JSON.stringify(dato, null, 1)) + '</pre>';
        }

        if (Array.isArray(dato)) {
            if (!dato.length) return '<span class="st-json-nulo">(vacío)</span>';

            // Array de objetos con la misma forma → tabla, que se lee mucho mejor
            var todosObj = dato.every(function (x) {
                return x && typeof x === 'object' && !Array.isArray(x);
            });
            if (todosObj && dato.length > 1) {
                var cols = [];
                dato.forEach(function (o) {
                    Object.keys(o).forEach(function (k) { if (cols.indexOf(k) < 0) cols.push(k); });
                });
                if (cols.length <= 6) {
                    var h = '<div class="table-responsive"><table class="st-json-tabla"><thead><tr>';
                    cols.forEach(function (c) { h += '<th>' + etiqueta(c) + '</th>'; });
                    h += '</tr></thead><tbody>';
                    dato.forEach(function (o) {
                        h += '<tr>';
                        cols.forEach(function (c) {
                            var v = o[c];
                            h += '<td>' + (v && typeof v === 'object' ? pintar(v, nivel + 2) : valor(v)) + '</td>';
                        });
                        h += '</tr>';
                    });
                    return h + '</tbody></table></div>';
                }
            }

            var lista = '<ul class="st-json-lista">';
            dato.forEach(function (x) {
                lista += '<li>' + (x && typeof x === 'object' ? pintar(x, nivel + 1) : valor(x)) + '</li>';
            });
            return lista + '</ul>';
        }

        if (dato && typeof dato === 'object') {
            var claves = Object.keys(dato);
            if (!claves.length) return '<span class="st-json-nulo">(vacío)</span>';
            var f = '<div class="st-json-ficha">';
            claves.forEach(function (k) {
                var v = dato[k];
                var esComplejo = v && typeof v === 'object';
                f += '<div class="st-json-fila' + (esComplejo ? ' is-bloque' : '') + '">'
                   + '<span class="st-json-k">' + etiqueta(k) + '</span>'
                   + '<span class="st-json-v">'
                   + (esComplejo ? pintar(v, nivel + 1) : valor(v))
                   + '</span></div>';
            });
            return f + '</div>';
        }

        return valor(dato);
    }

    /** Markdown mínimo, para lo que no es JSON. */
    function markdown(txt) {
        var h = escapar(txt);
        h = h.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
        h = h.replace(/^### (.+)$/gm, '<h5>$1</h5>');
        h = h.replace(/^## (.+)$/gm, '<h4>$1</h4>');
        h = h.replace(/^[-*] (.+)$/gm, '<li>$1</li>');
        h = h.replace(/(<li>[\s\S]*?<\/li>)/g, '<ul>$1</ul>');
        return h.replace(/\n{2,}/g, '<br/><br/>').replace(/\n/g, '<br/>');
    }

    /**
     * Re-pinta el contenido de un nodo. Si es JSON, lo convierte en fichas y
     * añade el original plegado; si no, le da formato de texto.
     */
    function aplicar(nodo, opciones) {
        if (!nodo || nodo.dataset.stJsonHecho === '1') return false;
        var crudo = nodo.textContent || '';
        if (!crudo.trim()) return false;

        var dato = extraer(crudo);
        if (!dato) {
            if (!opciones || opciones.soloJson !== true) {
                nodo.innerHTML = markdown(crudo);
                nodo.dataset.stJsonHecho = '1';
            }
            return false;
        }

        nodo.innerHTML = pintar(dato, 0);

        var det = document.createElement('details');
        det.className = 'st-json-ver';
        det.innerHTML = '<summary>Ver el original (JSON)</summary>'
                      + '<pre class="st-json-crudo">' + escapar(JSON.stringify(dato, null, 2)) + '</pre>';
        nodo.appendChild(det);
        nodo.dataset.stJsonHecho = '1';
        return true;
    }

    function aplicarTodos(selector, opciones) {
        var n = 0;
        document.querySelectorAll(selector).forEach(function (el) {
            if (aplicar(el, opciones)) n++;
        });
        return n;
    }

    global.StJson = {
        extraer: extraer,
        pintar: pintar,
        markdown: markdown,
        aplicar: aplicar,
        aplicarTodos: aplicarTodos
    };

    // Cualquier nodo marcado con data-st-json se pinta solo al cargar.
    document.addEventListener('DOMContentLoaded', function () {
        aplicarTodos('[data-st-json]');
    });
})(window);
