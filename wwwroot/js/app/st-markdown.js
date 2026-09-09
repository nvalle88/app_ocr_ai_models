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

    /* El caso que se está atendiendo. Lo fija la pantalla -StMarkdown.caso-
       porque la identidad no puede salir del texto que genera un modelo. */
    var caso = '';

    /* ── Ver en el mapa ───────────────────────────────────────────────────
       La direccion de un prestador no dice si queda cerca. El mapa si.

       Es el renderizador quien lo pone, NO el modelo: asi no puede escribir a
       donde apunta el enlace ni colar otra cosa. Solo se activa dentro de una
       celda de tabla, para no llenar de mapas un parrafo. */
    function enlaceMapa(direccion) {
        var d = (direccion || '').trim();
        if (d.length < 8) return '';                 // "Quito" no es una direccion
        return ' <a class="md-mapa" href="#" data-mapa="' + esc(d) + '"'
             + ' title="Ver en el mapa"><i class="fa fa-map-marker"></i> mapa</a>';
    }

    function carta(todo, texto, ruta) {
        if (!caso) return texto;                 // sin caso no hay enlace que funcione
        return '<a href="' + ruta + '&amp;caseCode=' + encodeURIComponent(caso) + '"'
             + ' target="_blank" rel="noopener noreferrer">' + texto + '</a>';
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
                     '$1<a href="$2" target="_blank" rel="noopener noreferrer">$2</a>')
            // -- La carta de una autorización -------------------------------
            //
            // La tool devuelve una ruta RELATIVA, y las reglas de arriba solo
            // aceptan http(s), así que sin esto el enlace de la carta llegaba
            // como texto plano y no había forma de bajar el PDF.
            //
            // Se acepta ESA ruta y ninguna más, con el id numérico y nada
            // detrás. Abrir la puerta a rutas relativas en general sería dejar
            // que el modelo escriba a dónde apunta un enlace que el afiliado va
            // a pulsar, y eso no lo decide un modelo.
            //
            // El caseCode lo pone la PÁGINA, no el texto: lo añade aquí el
            // navegador, que ya sabe de quién es el caso. Si no hay caso no se
            // hace enlace: el endpoint lo exige y un enlace roto es peor que un
            // texto.
            .replace(/\[([^\]]+)\]\((\/Studio\/ChatCliente\/Carta\?id=\d{1,12})\)/g, carta)
            .replace(/(^|[\s>])(\/Studio\/ChatCliente\/Carta\?id=\d{1,12})(?![\w=&])/g,
                     function (t, antes, ruta) { return antes + carta(t, 'Ver la carta', ruta); })
            // -- La carta de LIQUIDACIÓN de un sobre --------------------------
            //
            // Misma idea que la carta de autorización: una ruta relativa que las
            // reglas de http(s) de arriba no aceptan, así que sin esto el enlace
            // de descarga llegaba como texto plano y no se podía bajar el PDF.
            // El endpoint resuelve todo por el número de sobre, así que aquí NO
            // hace falta el caseCode. Se acepta SOLO esta ruta, con un número de
            // sobre NA-####, y nada más detrás: el modelo no decide a dónde
            // apunta un enlace que el afiliado va a pulsar.
            .replace(/\[([^\]]+)\]\((\/Studio\/ChatCliente\/CartaLiquidacion\?numeroSobre=NA-\d{1,12})\)/g,
                     '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>');
    }

    /* ── Distintivos en las celdas de estado ──────────────────────────────
       Una tabla donde "En su red" y "Fuera de su red" son texto plano se lee
       toda igual: hay que leer celda por celda para encontrar la que importa.

       No es adivinar: estas frases EXACTAS son las que el prompt le manda
       escribir al agente, asi que aqui solo se les pone el color que les
       corresponde. Lo que no esta en la lista se queda como texto —una tabla
       llena de colores inventados es peor que una sin ninguno—. */
    var ESTADOS = {
        'en su red':       'ok',    'en red':          'ok',
        'fuera de su red': 'aviso', 'fuera de red':    'aviso',
        'autorizada':      'ok',    'no autorizada':   'mal',
        'cubierto':        'ok',    'no cubierto':     'mal',
        'vigente':         'ok',    'no vigente':      'aviso',
        'en tramite':      'aviso', 'en trámite':      'aviso',
        'no consta':       'nada'
    };

    function marca(txt) {
        var k = txt.trim().toLowerCase().replace(/\s+/g, ' ');
        var tono = ESTADOS[k];
        if (!tono) return null;
        // "no consta" no es un estado bueno ni malo: es la ausencia del dato, y
        // se dice en gris para que no compita con lo que si consta.
        if (tono === 'nada') return '<span class="md-nada">' + esc(txt.trim()) + '</span>';
        return '<span class="md-marca ' + tono + '">' + esc(txt.trim()) + '</span>';
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
                            var m = marca(c);
                            /* Una celda que es una direccion se lleva su mapa.

                               La regla anterior aceptaba una COMA suelta como
                               senal, y con eso le colgaba un mapa a cualquier
                               celda con coma. En la tabla de reembolsos, donde
                               no hay una sola direccion, salian con enlace al
                               mapa cosas como "382,00 (presentado)" o
                               "Reembolso ambulatorio, ingresado".

                               Ahora hace falta una palabra que solo aparece en
                               direcciones de verdad, y se descarta lo que
                               empieza por cifra. Comprobado contra direcciones
                               reales de Co13SucursalesConvenio y contra las
                               celdas que fallaban: 6 de 6 y 10 de 10. */
                            var esDir = !num && !m && c.length > 18
                                        && !/^[$\s]*-?[\d.,]+/.test(c)
                                        && /(\bAv(\.|enida)?\b|\bCalle\b|\bCl\.|\bTorre\b|\bEdif|\bHospital\b|\bCl[ií]nica\b|\bCentro\s+(Comercial|M[eé]dico)\b|\bCiudadela\b|\bUrbanizaci[oó]n\b|\bManzana\b|\bMz\.|\bSolar\b|\bKm\.?\s*\d|\bLocal\s+\d|\bPiso\s+\d|\bOf\.)/i.test(c);
                            if (m) return '<td>' + m + '</td>';
                            return '<td' + (num ? ' class="num"' : '')
                                 + '>' + enLinea(c)
                                 + (esDir ? enlaceMapa(c) : '') + '</td>';
                        }).join('') + '</tr>';
                      }).join('')
                    + '</tbody></table></div>');
                continue;
            }

            /* Encabezado */
            var h = /^(#{1,4})\s+(.*)$/.exec(l);
            if (h) { out.push('<h4 class="md-h">' + enLinea(h[2]) + '</h4>'); i++; continue; }

            /* Cita: el texto literal del contrato.
               OJO con el orden: render() empieza por esc(texto), que convierte
               el > en &gt;. Buscar /^>/ despues de escapar NO acierta nunca, y
               la cita salia impresa tal cual -"> El periodo para presentar..."-
               con el signo delante. Se busca la forma ESCAPADA, que es la que
               existe a estas alturas. */
            if (/^&gt;\s?/.test(l)) {
                var cita = [];
                while (i < lineas.length && /^&gt;\s?/.test(lineas[i])) {
                    cita.push(enLinea(lineas[i].replace(/^&gt;\s?/, ''))); i++;
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

    /* ── HTML del modelo: lista blanca, no "permitir HTML" ───────────────────

       Se le puede dejar embeber estructura -tablas anidadas, celdas con varias
       lineas, columnas alineadas- sin abrir la puerta a inyeccion, y el truco es
       el ORDEN, otra vez:

         1. render() ya devolvio TODO escapado: <table> vino como &lt;table&gt; y
            <script> como &lt;script&gt;;
         2. aqui se DESESCAPAN solo las etiquetas de la lista blanca, y solo en su
            forma exacta: sin atributos, o con un class de la lista.

       Lo que no este en la lista se queda como letras. &lt;script&gt; sigue
       siendo texto. <img src=x onerror=alert(1)> sigue siendo texto, porque
       lleva atributos que no se admiten. No hay que confiar en la salida del
       modelo: hay que hacer que su confianza no importe.

       "Permitir HTML" a secas -lo que hacen casi todas las librerias de markdown
       con un flag- seria lo contrario: cualquier cosa que el modelo repita de un
       documento que le subieron pasaria a ejecutarse. */
    var PERMITIDAS = ['table','thead','tbody','tfoot','tr','th','td','caption',
                      'strong','b','em','i','u','small','br','hr','p','span','div',
                      'ul','ol','li','dl','dt','dd','h3','h4','h5','blockquote','code','pre'];

    /* Solo clases nuestras: nada de style ni de on*. */
    var CLASES = /^(md-[a-z-]+|num|nota|ok|mal|aviso)( (md-[a-z-]+|num|nota|ok|mal|aviso))*$/;

    /* Se cierran solo las etiquetas que se abrieron.
       Medido en la prueba: con <div onclick="..."> la apertura se quedaba
       escapada -bien- pero el </div> se liberaba, y un cierre suelto cierra el
       <p> de la burbuja y descuadra la pantalla. No es inyeccion, pero rompe el
       diseno, asi que se lleva una pila: un cierre solo pasa si arriba esta su
       apertura. */
    var SIN_CIERRE = ['br', 'hr'];

    function soltarPermitidas(html) {
        var pila = [];

        return html.replace(/&lt;(\/?)([a-zA-Z][a-zA-Z0-9]*)((?:\s+class=&quot;[^&]*?&quot;)?)\s*(\/?)&gt;/g,
            function (todo, cierre, tag, attrs, auto) {
                tag = tag.toLowerCase();
                if (PERMITIDAS.indexOf(tag) < 0) return todo;   // se queda como letras

                if (cierre) {
                    // Sin apertura no hay cierre: se queda como texto.
                    if (pila.length === 0 || pila[pila.length - 1] !== tag) return todo;
                    pila.pop();
                    return '</' + tag + '>';
                }

                var clase = '';
                if (attrs) {
                    var m = /class=&quot;([^&]*?)&quot;/.exec(attrs);
                    // Una clase que no reconocemos NO se copia: se ignora el
                    // atributo, no la etiqueta.
                    if (m && CLASES.test(m[1].trim())) clase = ' class="' + m[1].trim() + '"';
                }

                if (!auto && SIN_CIERRE.indexOf(tag) < 0) pila.push(tag);
                return '<' + tag + clase + (auto ? ' /' : '') + '>';
            });
    }

    /* La entrada de verdad: markdown Y estructura HTML de la lista blanca. */
    function renderRico(texto) {
        return soltarPermitidas(render(texto));
    }

    global.StMarkdown = {
        render: renderRico,
        soloMarkdown: render,
        escapar: esc,
        permitidas: PERMITIDAS,
        /* La pantalla dice qué caso se atiende, para el enlace de la carta. */
        set caso(v) { caso = (v || '').toString(); },
        get caso() { return caso; }
    };
})(window);
