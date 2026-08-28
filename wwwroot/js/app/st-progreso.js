/* ============================================================================
   REQ-020 — Decir qué se está haciendo mientras se hace

   El problema: las operaciones de esta aplicación tardan de verdad. Adjuntar
   documentos hace OCR de cada uno; revisar la cobertura llama al agente, que a
   su vez consulta media docena de APIs. Entre 20 y 90 segundos con la pantalla
   congelada. El cliente concluye que no funcionó y vuelve a pulsar.

   Lo que había: mostrarLoadingPanel('content', 'una frase fija'). Un spinner y
   una sola frase que no cambia nunca — y en el portal del afiliado, nada.

   Este módulo es deliberadamente HONESTO. Dos principios:

     1. NINGUNA barra de porcentaje inventada. Un 90% que no se mueve durante
        cuarenta segundos miente y desespera más que no poner nada. Lo que se
        muestra es la LISTA de etapas reales por las que pasa la operación, con
        la actual señalada, y el tiempo transcurrido de verdad.

     2. Cuando se PUEDE observar el avance real, se observa. Adjuntar sube los
        documentos de uno en uno, así que se sabe exactamente en cuál va;
        revisar la cobertura llega por SSE, así que se enseña qué está
        consultando el agente en ese instante. Ahí no hay estimación ninguna.

   Uso declarativo (para los formularios cortos):

       <form method="post" data-st-progreso
             data-st-titulo="Buscando sus contratos"
             data-st-etapas="Comprobando su cédula|Consultando sus contratos en Salud S.A."
             data-st-nota="Suele tardar unos segundos.">

   Uso por código (para lo que reporta avance real):

       var p = StProgreso.abrir({ titulo: '…', etapas: [...], nota: '…' });
       p.etapa(1);                     // señala la etapa 1 como la actual
       p.detalle('2 de 5 · factura.pdf');
       p.hecho(0);                     // marca la etapa 0 como terminada
       p.anadir('Consultando su deducible');   // etapa descubierta al vuelo
       p.error('No pudimos…');
       p.cerrar();
   ========================================================================= */
(function (global) {
    'use strict';

    var CAPA = null;      // el nodo del overlay, uno solo por página
    var ESTADO = null;

    function esc(s) {
        return String(s == null ? '' : s).replace(/[<>&]/g, function (c) {
            return { '<': '&lt;', '>': '&gt;', '&': '&amp;' }[c];
        });
    }

    function crearCapa() {
        var d = document.createElement('div');
        d.className = 'st-prog';
        d.setAttribute('role', 'status');
        d.setAttribute('aria-live', 'polite');
        d.innerHTML =
            '<div class="st-prog-caja">' +
              '<div class="st-prog-cab">' +
                '<span class="st-prog-anillo" aria-hidden="true"></span>' +
                '<div>' +
                  '<div class="st-prog-tit"></div>' +
                  '<div class="st-prog-detalle"></div>' +
                '</div>' +
              '</div>' +
              '<ul class="st-prog-etapas"></ul>' +
              '<div class="st-prog-checks-cab" hidden>Comprobaciones</div>' +
              '<ul class="st-prog-checks"></ul>' +
              '<div class="st-prog-pie">' +
                '<span class="st-prog-nota"></span>' +
                '<span class="st-prog-reloj">0s</span>' +
              '</div>' +
            '</div>';
        document.body.appendChild(d);
        return d;
    }

    function pintarEtapas() {
        if (!ESTADO) return;
        var ul = CAPA.querySelector('.st-prog-etapas');
        ul.innerHTML = ESTADO.etapas.map(function (txt, i) {
            var clase = i < ESTADO.actual ? 'is-hecha'
                      : i === ESTADO.actual ? 'is-actual'
                      : 'is-pendiente';
            if (ESTADO.hechas.indexOf(i) >= 0) clase = 'is-hecha';
            var icono = clase === 'is-hecha'  ? '<i class="fa fa-check"></i>'
                      : clase === 'is-actual' ? '<i class="fa fa-circle-o-notch fa-spin"></i>'
                      : '<i class="fa fa-circle-thin"></i>';
            return '<li class="' + clase + '">' + icono + '<span>' + esc(txt) + '</span></li>';
        }).join('');
    }

    function tic() {
        if (!ESTADO) return;
        var seg = Math.round((Date.now() - ESTADO.inicio) / 1000);
        var r = CAPA.querySelector('.st-prog-reloj');
        if (r) r.textContent = seg < 60 ? seg + 's'
                             : Math.floor(seg / 60) + 'm ' + (seg % 60) + 's';

        // Avance por tiempo SOLO cuando nadie reporta avance real, y nunca
        // sobre la ultima etapa: la ultima se queda trabajando hasta que la
        // operacion termina de verdad. Fingir que acabo seria mentir.
        if (ESTADO.autoAvance && ESTADO.actual < ESTADO.etapas.length - 1) {
            var toca = Math.floor(seg / ESTADO.segundosPorEtapa);
            if (toca > ESTADO.actual) {
                ESTADO.actual = Math.min(toca, ESTADO.etapas.length - 1);
                pintarEtapas();
            }
        }
    }

    /**
     * Las pestanas del caso viven en un iframe. Un overlay position:fixed
     * dentro del iframe solo taparia ese panel, y el resto de la pantalla
     * seguiria pareciendo utilizable. Si el padre tiene este mismo modulo, se
     * delega en el: asi el aviso cubre el area de trabajo entera, que es como
     * se comportaba el spinner que habia antes.
     */
    function delegado() {
        try {
            if (window.parent && window.parent !== window && window.parent.StProgreso) {
                return window.parent.StProgreso;
            }
        } catch (e) { /* otro origen: se pinta localmente */ }
        return null;
    }

    var api = {
        /**
         * Abre el overlay. `etapas` son las etapas REALES de la operacion, en
         * orden; si no se pasan, se muestra solo el titulo con el anillo.
         */
        abrir: function (opts) {
            var d = delegado();
            if (d) { d.abrir(opts); return d; }
            opts = opts || {};
            if (!CAPA) CAPA = crearCapa();

            ESTADO = {
                etapas: (opts.etapas || []).slice(),
                actual: 0,
                hechas: [],
                inicio: Date.now(),
                autoAvance: opts.autoAvance !== false,
                segundosPorEtapa: opts.segundosPorEtapa || 6,
                timer: null
            };

            CAPA.querySelector('.st-prog-tit').textContent = opts.titulo || 'Trabajando…';
            CAPA.querySelector('.st-prog-detalle').textContent = opts.detalle || '';
            CAPA.querySelector('.st-prog-nota').textContent = opts.nota || '';
            CAPA.classList.remove('is-error');
            CAPA.classList.add('is-visible');
            pintarEtapas();
            tic();
            ESTADO.timer = setInterval(tic, 1000);

            // Con el overlay puesto no se puede tocar nada de detrás.
            document.body.classList.add('st-prog-bloqueado');
            return api;
        },

        /** Señala qué etapa está en curso ahora (0-based). Corta el auto-avance. */
        etapa: function (i, detalle) {
            if (!ESTADO) return api;
            ESTADO.autoAvance = false;
            ESTADO.actual = Math.max(0, Math.min(i, ESTADO.etapas.length - 1));
            if (detalle !== undefined) api.detalle(detalle);
            pintarEtapas();
            return api;
        },

        /** Marca una etapa como terminada de verdad. */
        hecho: function (i) {
            if (!ESTADO) return api;
            if (ESTADO.hechas.indexOf(i) < 0) ESTADO.hechas.push(i);
            pintarEtapas();
            return api;
        },

        /** Añade una etapa descubierta durante la ejecución y la pone en curso. */
        anadir: function (texto) {
            if (!ESTADO) return api;
            ESTADO.autoAvance = false;
            ESTADO.etapas.push(texto);
            ESTADO.actual = ESTADO.etapas.length - 1;
            pintarEtapas();
            return api;
        },

        /**
         * Una comprobación REAL que el agente acaba de hacer, con su resultado.
         *
         * Va en su propia lista, separada de las etapas gruesas, porque son dos
         * cosas distintas: la etapa es por dónde va el proceso; esto es qué se
         * verificó y qué salió. Mezclarlas haría ilegibles las dos.
         *
         * Cada línea existe porque hay una llamada registrada. Si el agente no
         * consulta nada, aquí no aparece nada.
         *
         *   p.verificacion({ que: 'Comprobando su factura en el SRI',
         *                    resultado: 'Está registrada', tono: 'ok', segundos: 1.4 })
         */
        verificacion: function (v) {
            if (!CAPA || !v || !v.que) return api;
            var ul  = CAPA.querySelector('.st-prog-checks');
            var cab = CAPA.querySelector('.st-prog-checks-cab');
            if (!ul) return api;

            // Sin id no se puede evitar repetir, así que se exige.
            if (v.id !== undefined && ul.querySelector('[data-id="' + v.id + '"]')) return api;

            var tono = { ok: 'is-ok', atencion: 'is-atencion', fallo: 'is-fallo' }[v.tono] || 'is-neutro';
            var icono = { ok: 'fa-check', atencion: 'fa-exclamation', fallo: 'fa-times' }[v.tono] || 'fa-circle-o';

            var li = document.createElement('li');
            li.className = tono;
            if (v.id !== undefined) li.setAttribute('data-id', v.id);
            li.innerHTML =
                '<i class="fa ' + icono + '" aria-hidden="true"></i>' +
                '<span class="st-check-que">' + esc(v.que) + '</span>' +
                (v.resultado ? '<span class="st-check-res">' + esc(v.resultado) + '</span>' : '') +
                (v.segundos ? '<span class="st-check-t">' + v.segundos + 's</span>' : '');
            ul.appendChild(li);
            if (cab) cab.hidden = false;

            // Lo último hecho es lo que interesa ver.
            ul.scrollTop = ul.scrollHeight;
            return api;
        },

        /** La línea pequeña bajo el título: "3 de 7 · factura.pdf". */
        detalle: function (txt) {
            if (!CAPA) return api;
            CAPA.querySelector('.st-prog-detalle').textContent = txt || '';
            return api;
        },

        nota: function (txt) {
            if (!CAPA) return api;
            CAPA.querySelector('.st-prog-nota').textContent = txt || '';
            return api;
        },

        /** Deja el overlay en rojo con el motivo. No se cierra solo: el
         *  usuario tiene que poder leerlo. */
        error: function (msg) {
            if (!CAPA) return api;
            if (ESTADO && ESTADO.timer) clearInterval(ESTADO.timer);
            CAPA.classList.add('is-error');
            CAPA.querySelector('.st-prog-tit').textContent = 'No se pudo completar';
            CAPA.querySelector('.st-prog-detalle').textContent = msg || '';
            var ul = CAPA.querySelector('.st-prog-etapas');
            ul.innerHTML = '<li class="is-error"><i class="fa fa-times"></i>' +
                           '<span>' + esc(msg || 'Error inesperado') + '</span></li>' +
                           '<li class="is-pendiente"><i class="fa fa-refresh"></i>' +
                           '<span>Cierre este aviso y vuelva a intentarlo.</span></li>';
            var cerrar = document.createElement('button');
            cerrar.type = 'button';
            cerrar.className = 'st-prog-cerrar';
            cerrar.textContent = 'Cerrar';
            cerrar.onclick = function () { api.cerrar(); };
            CAPA.querySelector('.st-prog-caja').appendChild(cerrar);
            return api;
        },

        cerrar: function () {
            if (ESTADO && ESTADO.timer) clearInterval(ESTADO.timer);
            ESTADO = null;
            if (CAPA) {
                CAPA.classList.remove('is-visible', 'is-error');
                var b = CAPA.querySelector('.st-prog-cerrar');
                if (b) b.parentNode.removeChild(b);
            }
            document.body.classList.remove('st-prog-bloqueado');
            return api;
        },

        abierto: function () { return !!ESTADO; }
    };

    /**
     * Pregunta antes de una acción que no tiene vuelta atrás.
     *
     * Usa SweetAlert2, que es lo que ya emplea el resto del producto y está
     * cargado en el layout base. Si por lo que sea no estuviera disponible, cae
     * al confirm del navegador: es feo, pero preguntar importa más que la
     * estética, y quedarse sin preguntar sería lo único inaceptable.
     */
    function confirmar(f) {
        var titulo  = f.dataset.stConfirmarTitulo || '¿Seguro?';
        var texto   = f.dataset.stConfirmar || '';
        var ok      = f.dataset.stConfirmarOk || 'Sí, continuar';
        var detalle = f.dataset.stConfirmarDetalle || '';

        if (!global.Swal || typeof global.Swal.fire !== 'function') {
            return Promise.resolve(window.confirm(titulo + '\n\n' + texto));
        }

        var html = '<p style="font-size:14.5px;line-height:1.55;color:#1f2733;margin:0;">'
                 + esc(texto) + '</p>';
        if (detalle) {
            html += '<p style="font-size:13px;line-height:1.5;color:#6b7684;'
                  + 'background:#f6f8fb;border-radius:6px;padding:10px 12px;margin:14px 0 0;'
                  + 'text-align:left;">' + esc(detalle) + '</p>';
        }

        return global.Swal.fire({
            title: titulo,
            html: html,
            icon: f.dataset.stConfirmarIcono || 'warning',
            showCancelButton: true,
            confirmButtonText: ok,
            cancelButtonText: f.dataset.stConfirmarNo || 'No, dejarlo como está',
            reverseButtons: true,               // el destructivo, lejos del pulgar
            focusCancel: true,                  // no se confirma sin querer
            confirmButtonColor: f.dataset.stConfirmarColor || '#a4262c',
            cancelButtonColor: '#6b7684',
            customClass: { popup: 'st-confirm' }
        }).then(function (r) { return !!(r && r.isConfirmed); });
    }

    // ── Modo declarativo ─────────────────────────────────────────────────
    // Cualquier formulario con data-st-progreso muestra el overlay al enviarse
    // y queda blindado contra el doble envío, que es el otro sintoma del mismo
    // problema: sin senal de vida, la gente pulsa dos veces y se duplica todo.
    function engancharFormularios() {
        document.querySelectorAll('form[data-st-progreso]').forEach(function (f) {
            if (f.dataset.stProgEngan === '1') return;
            f.dataset.stProgEngan = '1';

            f.addEventListener('submit', function (ev) {
                if (f.dataset.stProgEnviado === '1') {   // segundo clic: se ignora
                    ev.preventDefault();
                    return;
                }

                // Si el formulario pide confirmación, se pregunta ANTES de abrir
                // el aviso: si no, el overlay aparece aunque la persona cancele
                // y la pantalla se queda bloqueada sin que pase nada.
                if (f.dataset.stConfirmar && f.dataset.stConfirmado !== '1') {
                    ev.preventDefault();
                    confirmar(f).then(function (si) {
                        if (!si) return;
                        // Se marca y se reenvía: al volver a entrar aquí ya no
                        // vuelve a preguntar y sigue su curso normal.
                        f.dataset.stConfirmado = '1';
                        if (typeof f.requestSubmit === 'function') f.requestSubmit();
                        else f.submit();
                    });
                    return;
                }

                f.dataset.stProgEnviado = '1';

                f.querySelectorAll('button[type=submit], input[type=submit]').forEach(function (b) {
                    b.disabled = true;
                });

                var etapas = (f.dataset.stEtapas || '')
                    .split('|').map(function (x) { return x.trim(); })
                    .filter(function (x) { return x.length > 0; });

                api.abrir({
                    titulo: f.dataset.stTitulo || 'Trabajando…',
                    etapas: etapas,
                    nota: f.dataset.stNota || '',
                    segundosPorEtapa: parseInt(f.dataset.stSegundos || '6', 10)
                });
            });
        });
    }

    api.enganchar = engancharFormularios;
    global.StProgreso = api;

    document.addEventListener('DOMContentLoaded', engancharFormularios);

    // Si el usuario vuelve con el botón atrás, el navegador restaura la página
    // desde caché con el overlay puesto y el formulario bloqueado. Hay que
    // deshacerlo o la pantalla queda inservible.
    window.addEventListener('pageshow', function (e) {
        if (e.persisted) {
            api.cerrar();
            document.querySelectorAll('form[data-st-progreso]').forEach(function (f) {
                delete f.dataset.stProgEnviado;
                delete f.dataset.stConfirmado;
                f.querySelectorAll('button[type=submit], input[type=submit]').forEach(function (b) {
                    b.disabled = false;
                });
            });
        }
    });
})(window);
