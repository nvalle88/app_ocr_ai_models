

function Init_Touchspin() {
    $('.touchspin_tasa').TouchSpin({
        min: 0.00,
        max: 99999999999,
        decimals: 2,
        prefix: '$',
        step: 0.01,
        buttondown_class: 'btn btn-primary',
        buttonup_class: 'btn btn-primary'
    });
}

function Init_Select2() {
    $('select').select2({
        theme: 'classic',
        allowClear: false,
        placeholder: 'Seleccione...',
        language: 'es'
    });
}

function cargando() {
    mostrarLoadingPanel("content", "");
}



function mostrarLoadingPanel(idElemento, texto) {
    $('#' + idElemento).waitMe({
        effect: 'bounce',
        text: texto,
        bg: 'rgba(255, 255, 255, 0.5)',
        color: '#000000',
        fontSize: '15px'
    });
}

 //color: '#eb5c27',

function mostrarNotificacion(titulo, texto) {
    var color = "";
    var icon = "";
    switch (titulo) {
        case "Información": color = "#3276B1"; icon = "exclamation-circle"; break;
        case "Error": color = "#C46A69"; icon = "thumbs-o-down"; break;
        case "Aviso": color = "#c79121"; icon = "exclamation-triangle"; break;
        case "Satisfactorio": color = "#3276B1"; icon = "thumbs-o-up"; break;
    }
    $.smallBox({
        title: titulo,
        content: texto,
        color: color,
        icon: "fa fa-" + icon + " shake animated",
        timeout: 6000
    });
}

function mostrarNotificacionTimer(titulo, texto, timer) {
    var color = "";
    var icon = "";
    switch (titulo) {
        case "Información": color = "#3276B1"; icon = "exclamation-circle"; break;
        case "Error": color = "#C46A69"; icon = "fa-thumbs-o-down"; break;
        case "Aviso": color = "#c79121"; icon = "exclamation-triangle"; break;
        case "Satisfactorio": color = "#1D6922"; icon = "fa-thumbs-o-up"; break;
    }
    $.smallBox({
        title: titulo,
        content: texto,
        color: color,
        icon: "fa fa-" + icon + " shake animated",
        timeout: timer
    });
}

function Asignar_Codigo_Barras(idElemento, valor) {
    JsBarcode("#" + idElemento, valor, {
        format: "CODE128",
        displayValue: true,
        fontSize: 20
    });
}
function Init_FileInput(idElemento) {
    $("#" + idElemento).fileinput({
        showUpload: false,
        language: 'es'
    });
}
function obtenerIdAjax(id) {
    try {
        return parseInt(id);
    } catch (e) {
        return -1;
    }
}

function Gestionar_Msg() {
    var mensaje = $("#span_mensaje").html();
    if (mensaje != "" && mensaje != null) {
        var arr_msg = mensaje.split('|');
        mostrarNotificacion(arr_msg[0], arr_msg[1]);
    }

    Gestionar_Msg_Timer();
}

function Gestionar_Msg_Timer() {
    var mensaje2 = $("#span_mensaje_timer").html();
    if (mensaje2 != "" && mensaje2 != null) {
        var arr_msg2 = mensaje2.split('|');

        mostrarNotificacionTimer(arr_msg2[0], arr_msg2[1], arr_msg2[2]);
    }
}



function initLoadingForm() {
    $(this).on("submit", function (event) {
        if (!$(event.target).hasClass("noFormLoading")) {
            $("#btn-guardar").prop("disabled", "disabled");
            $("#btn-guardar").html("<i class='fa fa-spinner fa-spin'></i> " + $("#btn-guardar").html());
        }
    });
}

function tablamodal() {

    var responsiveHelper_dt_basic = undefined;
    var responsiveHelper_datatable_fixed_column = undefined;
    var responsiveHelper_datatable_col_reorder = undefined;
    var responsiveHelper_datatable_tabletools = undefined;

    var breakpointDefinition = {
        tablet: 1024,
        phone: 480
    };

    $('#dt_basic').dataTable({
        "sDom": "<'dt-toolbar'<'col-xs-12 col-sm-6'f><'col-sm-6 col-xs-12 hidden-xs'l>r>" +
            "t" +
            "<'dt-toolbar-footer'<'col-sm-6 col-xs-12 hidden-xs'i><'col-xs-12 col-sm-6'p>>",
        "autoWidth": true,
        "preDrawCallback": function () {
            if (!responsiveHelper_dt_basic) {
                responsiveHelper_dt_basic = new ResponsiveDatatablesHelper($('#dt_basic'), breakpointDefinition);
            }
        },
        "rowCallback": function (nRow) {
            responsiveHelper_dt_basic.createExpandIcon(nRow);
        },
        "drawCallback": function (oSettings) {
            responsiveHelper_dt_basic.respond();
        }
    });
    var otable = $('#datatable_fixed_column').DataTable({
        "sDom": "<'dt-toolbar'<'col-xs-12 col-sm-6 hidden-xs'f><'col-sm-6 col-xs-12 hidden-xs'<'toolbar'>>r>" +
            "t" +
            "<'dt-toolbar-footer'<'col-sm-6 col-xs-12 hidden-xs'i><'col-xs-12 col-sm-6'p>>",
        "autoWidth": true,
        "preDrawCallback": function () {
            if (!responsiveHelper_datatable_fixed_column) {
                responsiveHelper_datatable_fixed_column = new ResponsiveDatatablesHelper($('#datatable_fixed_column'), breakpointDefinition);
            }
        },
        "rowCallback": function (nRow) {
            responsiveHelper_datatable_fixed_column.createExpandIcon(nRow);
        },
        "drawCallback": function (oSettings) {
            responsiveHelper_datatable_fixed_column.respond();
        }

    });
    $("div.toolbar").html('<div class="text-right"><img src="/img/logo.png" alt="SmartAdmin" style="width: 111px; margin-top: 3px; margin-right: 10px;"></div>');
    $("#datatable_fixed_column thead th input[type=text]").on('keyup change', function () {

        otable
            .column($(this).parent().index() + ':visible')
            .search(this.value)
            .draw();

    });
    $('#datatable_col_reorder').dataTable({
        "sDom": "<'dt-toolbar'<'col-xs-12 col-sm-6'f><'col-sm-6 col-xs-6 hidden-xs'C>r>" +
            "t" +
            "<'dt-toolbar-footer'<'col-sm-6 col-xs-12 hidden-xs'i><'col-sm-6 col-xs-12'p>>",
        "autoWidth": true,
        "preDrawCallback": function () {
            if (!responsiveHelper_datatable_col_reorder) {
                responsiveHelper_datatable_col_reorder = new ResponsiveDatatablesHelper($('#datatable_col_reorder'), breakpointDefinition);
            }
        },
        "rowCallback": function (nRow) {
            responsiveHelper_datatable_col_reorder.createExpandIcon(nRow);
        },
        "drawCallback": function (oSettings) {
            responsiveHelper_datatable_col_reorder.respond();
        }
    });
    $('#datatable_tabletools').dataTable({
        "sDom": "<'dt-toolbar'<'col-xs-12 col-sm-6'f><'col-sm-6 col-xs-6 hidden-xs'T>r>" +
            "t" +
            "<'dt-toolbar-footer'<'col-sm-6 col-xs-12 hidden-xs'i><'col-sm-6 col-xs-12'p>>",
        "oTableTools": {
            "aButtons": [
                "copy",
                "csv",
                "xls",
                {
                    "sExtends": "pdf",
                    "sTitle": "SmartAdmin_PDF",
                    "sPdfMessage": "SmartAdmin PDF Export",
                    "sPdfSize": "letter"
                },
                {
                    "sExtends": "print",
                    "sMessage": "Generated by SmartAdmin <i>(press Esc to close)</i>"
                }
            ],
            "sSwfPath": "/js/plugin/datatables/swf/copy_csv_xls_pdf.swf"
        },
        "autoWidth": true,
        "preDrawCallback": function () {
            if (!responsiveHelper_datatable_tabletools) {
                responsiveHelper_datatable_tabletools = new ResponsiveDatatablesHelper($('#datatable_tabletools'), breakpointDefinition);
            }
        },
        "rowCallback": function (nRow) {
            responsiveHelper_datatable_tabletools.createExpandIcon(nRow);
        },
        "drawCallback": function (oSettings) {
            responsiveHelper_datatable_tabletools.respond();
        }
    });

}