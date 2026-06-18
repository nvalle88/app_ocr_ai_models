using app_ocr_ai_models.Services.Zendesk;
using app_tramites.Models.ModelAi;

namespace app_ocr_ai_models.Areas.Studio.Models
{
    // ============================================================
    // REQ-019 T4 — ViewModels del Área Studio para importar sobres.
    // ============================================================

    /// <summary>
    /// Formulario de búsqueda/importación de un sobre Zendesk.
    /// </summary>
    public class ImportarSobreViewModel
    {
        /// <summary>Número del sobre o ID de ticket Zendesk a importar.</summary>
        public string? NumeroSobre { get; set; }

        /// <summary>ID numérico del ticket (alternativa a NumeroSobre).</summary>
        public long? TicketId { get; set; }

        /// <summary>Cuenta Zendesk donde reside el ticket (null = buscar en todas).</summary>
        public ZendeskCuenta? Cuenta { get; set; }

        /// <summary>Código del Process (definición de caso) al que se asignará el caso importado.</summary>
        public string ProcessCode { get; set; } = string.Empty;

        /// <summary>Lista de procesos disponibles para el selector.</summary>
        public IReadOnlyList<Process> ProcesosDisponibles { get; set; } = Array.Empty<Process>();
    }

    /// <summary>
    /// Resultado de la búsqueda de tickets para confirmar la importación.
    /// </summary>
    public class BusquedaSobreResultViewModel
    {
        /// <summary>Número de sobre buscado.</summary>
        public string? NumeroSobreBuscado { get; set; }

        /// <summary>Cédula buscada (alternativa).</summary>
        public string? CedulaBuscada { get; set; }

        /// <summary>Resultados agrupados por cuenta.</summary>
        public IReadOnlyList<ZendeskBusquedaResultDto> Resultados { get; set; }
            = Array.Empty<ZendeskBusquedaResultDto>();

        /// <summary>Código del Process al que se asignará el caso importado.</summary>
        public string ProcessCode { get; set; } = string.Empty;

        /// <summary>Mensaje de error, si aplica.</summary>
        public string? Error { get; set; }
    }

    /// <summary>
    /// Vista de confirmación antes de importar: muestra detalle del ticket + adjuntos.
    /// </summary>
    public class ConfirmarImportViewModel
    {
        /// <summary>Detalle del ticket a importar.</summary>
        public ZendeskTicketDto Ticket { get; set; } = null!;

        /// <summary>Cuenta donde reside el ticket.</summary>
        public ZendeskCuenta Cuenta { get; set; }

        /// <summary>Comentarios del ticket con sus adjuntos.</summary>
        public IReadOnlyList<ZendeskComentarioDto> Comentarios { get; set; }
            = Array.Empty<ZendeskComentarioDto>();

        /// <summary>Código del Process al que se asignará el caso importado.</summary>
        public string ProcessCode { get; set; } = string.Empty;

        /// <summary>Total de adjuntos en todos los comentarios.</summary>
        public int TotalAdjuntos => Comentarios.Sum(c => c.Attachments.Count);
    }

    /// <summary>
    /// Vista del Caso importado con sus DataFiles.
    /// </summary>
    public class CasoImportadoViewModel
    {
        /// <summary>Caso OCR creado/cargado.</summary>
        public ProcessCase Caso { get; set; } = null!;

        /// <summary>DataFiles del caso con texto OCR.</summary>
        public IReadOnlyList<DataFile> Archivos { get; set; } = Array.Empty<DataFile>();

        /// <summary>Notas del ticket importadas como Note del caso.</summary>
        public IReadOnlyList<Note> Notas { get; set; } = Array.Empty<Note>();

        /// <summary>Código del Process (definición).</summary>
        public string ProcessCode { get; set; } = string.Empty;

        /// <summary>Mensaje de éxito o info.</summary>
        public string? Mensaje { get; set; }

        /// <summary>Indica si hubo archivos que no pudieron procesarse (OCR falló).</summary>
        public IReadOnlyList<string> AdvertenciasOcr { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Solicitud para adjuntar documentos adicionales a un caso ya importado.
    /// </summary>
    public class AdjuntarDocumentosRequest
    {
        /// <summary>CaseCode del caso al que se adjuntan los documentos.</summary>
        public Guid CaseCode { get; set; }

        /// <summary>Lista de archivos en base64 para ingestar.</summary>
        public IReadOnlyList<ArchivoBase64Input> Archivos { get; set; }
            = Array.Empty<ArchivoBase64Input>();
    }

    /// <summary>
    /// Archivo individual en base64 para adjuntar a un caso.
    /// </summary>
    public class ArchivoBase64Input
    {
        /// <summary>Nombre del archivo (con extensión).</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>Contenido en Base64.</summary>
        public string ContentBase64 { get; set; } = string.Empty;

        /// <summary>Extensión del archivo (p. ej. ".pdf").</summary>
        public string Extension { get; set; } = string.Empty;
    }

    /// <summary>
    /// Respuesta JSON al adjuntar documentos adicionales.
    /// </summary>
    public class AdjuntarDocumentosResponse
    {
        /// <summary>Indica si la operación fue exitosa.</summary>
        public bool Ok { get; set; }

        /// <summary>IDs de los DataFile creados.</summary>
        public IReadOnlyList<int> DataFileIds { get; set; } = Array.Empty<int>();

        /// <summary>Advertencias por archivos que no pudieron procesarse.</summary>
        public IReadOnlyList<string> Advertencias { get; set; } = Array.Empty<string>();

        /// <summary>Mensaje de error global, si aplica.</summary>
        public string? Error { get; set; }
    }
}
