using app_ocr_ai_models.Services.Zendesk;
using app_tramites.Models.ModelAi;

namespace app_ocr_ai_models.Areas.Studio.Models
{
    // ============================================================
    // REQ-019 T4 — ViewModels del Área Studio para importar sobres.
    // REQ-019 T22 — ViewModels adicionales para origen Armonix.
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

    // ----------------------------------------------------------------
    // REQ-019 T22 — ViewModels para la fuente documental Armonix
    // ----------------------------------------------------------------

    /// <summary>
    /// Formulario simplificado (T22 RW): solo número de sobre o cédula
    /// más el proceso destino. Los identificadores de contrato se resuelven
    /// automáticamente via <c>BuscarSobre</c> de api-armonix.
    /// </summary>
    public class ImportarSobreArmonixViewModel
    {
        /// <summary>Número del sobre en Armonix/MFiles (se usa como criterio de búsqueda).</summary>
        public string? NumeroSobre { get; set; }

        /// <summary>Cédula del afiliado/paciente (alternativa al número de sobre).</summary>
        public string? Cedula { get; set; }

        /// <summary>Código del Process (definición de caso) al que se asignará el caso importado.</summary>
        public string ProcessCode { get; set; } = string.Empty;

        /// <summary>Lista de procesos disponibles para el selector.</summary>
        public IReadOnlyList<Process> ProcesosDisponibles { get; set; } = Array.Empty<Process>();
    }

    /// <summary>
    /// Sobre resuelto para mostrar en la tabla de selección cuando la búsqueda
    /// devuelve más de un resultado.
    /// </summary>
    public class SobreArmonixResueltoViewModel
    {
        /// <summary>Número del sobre.</summary>
        public string NumeroSobre { get; set; } = string.Empty;

        /// <summary>Nombre del titular/afiliado.</summary>
        public string NombreTitular { get; set; } = string.Empty;

        /// <summary>Estado del sobre.</summary>
        public string EstadoSobre { get; set; } = string.Empty;

        /// <summary>Fecha de recepción del sobre.</summary>
        public DateTime? FechaRecepcion { get; set; }

        // Identificadores resueltos (hidden en la tabla de selección)

        /// <summary>Código de región resuelto.</summary>
        public string CodigoRegion { get; set; } = string.Empty;

        /// <summary>Código de producto resuelto.</summary>
        public string CodigoProducto { get; set; } = string.Empty;

        /// <summary>Número de contrato resuelto.</summary>
        public string NumeroContrato { get; set; } = string.Empty;

        /// <summary>Número de persona/paciente resuelto.</summary>
        public string NumeroPersonaPaciente { get; set; } = string.Empty;
    }

    /// <summary>
    /// Resultado de la búsqueda de sobres en Armonix (T22 RW).
    /// Puede contener cero, uno o varios sobres.
    /// </summary>
    public class BusquedaSobreArmonixViewModel
    {
        /// <summary>Criterio de búsqueda usado (número de sobre o cédula).</summary>
        public string CriterioBuscado { get; set; } = string.Empty;

        /// <summary>Sobres encontrados con identificadores ya resueltos.</summary>
        public IReadOnlyList<SobreArmonixResueltoViewModel> Sobres { get; set; }
            = Array.Empty<SobreArmonixResueltoViewModel>();

        /// <summary>Código del Process destino.</summary>
        public string ProcessCode { get; set; } = string.Empty;

        /// <summary>Mensaje de error, si aplica.</summary>
        public string? Error { get; set; }
    }

    /// <summary>
    /// Previsualización de documentos de un sobre ya resuelto antes de confirmar la importación.
    /// </summary>
    public class VistaDocumentosArmonixViewModel
    {
        /// <summary>Número de sobre consultado.</summary>
        public string NumeroSobre { get; set; } = string.Empty;

        /// <summary>Nombre del titular para mostrar.</summary>
        public string NombreTitular { get; set; } = string.Empty;

        // Identificadores resueltos (se pasan como hidden al ImportarDesdeArmonix)

        /// <summary>Número de contrato resuelto.</summary>
        public string? NumeroContrato { get; set; }

        /// <summary>Código de producto resuelto.</summary>
        public string? CodigoProducto { get; set; }

        /// <summary>Código de región resuelto.</summary>
        public string? CodigoRegion { get; set; }

        /// <summary>Número de persona/paciente resuelto.</summary>
        public string? NumeroPersonaPaciente { get; set; }

        /// <summary>Nombres/IDs de los documentos disponibles en MFiles.</summary>
        public IReadOnlyList<string> DocumentosDisponibles { get; set; } = Array.Empty<string>();

        /// <summary>Código del Process destino.</summary>
        public string ProcessCode { get; set; } = string.Empty;

        /// <summary>Mensaje de error, si aplica.</summary>
        public string? Error { get; set; }
    }
}
