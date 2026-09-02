/* ============================================================================
   Formato para las respuestas del chat.

   El agente escribe markdown —tablas, negrita, citas del contrato— y sin esto
   al afiliado le llegaba el asterisco y el pipe en crudo. Una tabla de importes
   escrita con | y sin renderizar es ilegible, que es peor que no haberla hecho.

   -- Por qué no una librería ------------------------------------------------
   Porque hace falta muy poco y porque el orden importa para la seguridad:
   PRIMERO se escapa todo el texto, DESPUÉS se aplica el formato sobre lo ya
   escapado. Así una respuesta que traiga <script> o un onerror= sale como
   letras, no como HTML. Una librería que acepta HTML embebido —casi todas por
   defecto— sería justo lo contrario en una pantalla que muestra texto generado.

   Soporta lo que el chat usa de verdad: encabezados, negrita, cursiva, código,
   listas, citas y TABLAS. Nada más, a propósito.
   ========================================================================= */
(function (global) {
    'use strict';

    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    /* Formato de una línea: se aplica SOBRE TEXTO YA ESCAPADO. */
    function enLinea(t) {
        return t
            .replace(/`([^`]+)`/g, '<code>$1</code>')
            .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
            .replace(/(^|[\s(])\*([^*\n]+)\*/g, '$1<em>$2</em>')
            // Sólo http y https: un href javascript: no entra.
            .replace(/\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)/g,
                     '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>')
            // Un enlace suelto también se vuelve clicable: si el contrato viene
            // como URL pelada, el afiliado tiene que poder abrirlo.
            .replace(/(^|[\s>])(https?:\/\/[^\s<]+)/g,
                     '$1<a href="$2" target="_blank" rel="noopener noreferrer">$2</a>');
    }

    function esSeparadorDeTabla(l) {
        return /^\s*\|?[\s:-]*-[\s|:-]*\|?\s*$/.test(l) && l.indexOf('-') >= 0;
    }

    function celdas(l) {
        var t = l.trim().replace(/^\|/, '').replace(/\|$/, '');
        return t.split('|').map(function (c) { return c.trim(); });
    }

    function render(texto) {
        var lineas = esc(texto).replace(/\r/g, '').split('\n');
        var out = [];
        var i = 0;

        while (i < lineas.length) {
            var l = lineas[i];

            /* Tabla: una fila con pipes y debajo el separador. */
            if (l.indexOf('|') >= 0 && i + 1 < lineas.length && esSeparadorDeTabla(lineas[i + 1])) {
                var cab = celdas(l);
                i += 2;
                var filas = [];
                while (i < lineas.length && lineas[i].indexOf('|') >= 0 && lineas[i].trim() !== '') {
                    filas.push(celdas(lineas[i])); i++;
                }
                out.push('<div class="md-tabla-wrap"><table class="md-tabla"><thead><tr>'
                    + cab.map(function (c) { return '<th>' + enLinea(c) + '</th>'; }).join('')
                    + '</tr></thead><tbody>'
                    + filas.map(function (f) {
                        return '<tr>' + f.map(function (c) {
                            // Lo que parece dinero o porcentaje se alinea a la
                            // derecha: una columna de importes desalineada no se
                            // puede comparar de un vistazo.
                            var num = /^[$\s]*-?[\d.,]+\s*%?$/.test(c);
                            return '<td' + (num ? ' class="num"' : '') + '>' + enLinea(c) + '</td>';
                        }).join('') + '</tr>';
                      }).join('')
                    + '</tbody></table></div>');
                continue;
            }

            /* Encabezado */
            var h = /^(#{1,4})\s+(.*)$/.exec(l);
            if (h) { out.push('<h4 class="md-h">' + enLinea(h[2]) + '</h4>'); i++; continue; }

            /* Cita: el texto literal del contrato */
            if (/^>\s?/.test(l)) {
                var cita = [];
                while (i < lineas.length && /^>\s?/.test(lineas[i])) {
                    cita.push(enLinea(lineas[i].replace(/^>\s?/, ''))); i++;
                }
                out.push('<blockquote class="md-cita">' + cita.join('<br>') + '</blockquote>');
                continue;
            }

            /* Lista */
            if (/^\s*([-*•]|\d+\.)\s+/.test(l)) {
                var ord = /^\s*\d+\./.test(l);
                var items = [];
                while (i < lineas.length && /^\s*([-*•]|\d+\.)\s+/.test(lineas[i])) {
                    items.push('<li>' + enLinea(lineas[i].replace(/^\s*([-*•]|\d+\.)\s+/, '')) + '</li>');
                    i++;
                }
                out.push('<' + (ord ? 'ol' : 'ul') + ' class="md-lista">' + items.join('') + '</'
                         + (ord ? 'ol' : 'ul') + '>');
                continue;
            }

            /* Párrafo: se juntan las líneas seguidas */
            if (l.trim() === '') { i++; continue; }
            var par = [];
            while (i < lineas.length && lineas[i].trim() !== ''
                   && !/^(#{1,4}\s|>\s?|\s*([-*•]|\d+\.)\s)/.test(lineas[i])
                   && lineas[i].indexOf('|') < 0) {
                par.push(enLinea(lineas[i])); i++;
            }
            if (par.length) out.push('<p class="md-p">' + par.join(' ') + '</p>');
            else i++;
        }

        return out.join('');
    }

    global.StMarkdown = { render: render, escapar: esc };
})(window);
