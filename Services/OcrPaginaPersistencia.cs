using app_tramites.Models.ModelAi;
using app_tramites.Models.ViewModel;

namespace app_ocr_ai_models.Services
{
    /// <summary>
    /// Convierte las <see cref="PaginaOcr"/> devueltas por
    /// <see cref="IOcrIngestService.ProcessFileDetailedAsync"/> en filas
    /// <see cref="DataFilePage"/> listas para insertar. Evita duplicar esta
    /// lógica en cada caller (Armonix, Zendesk y adjuntar).
    /// </summary>
    public static class OcrPaginaPersistencia
    {
        /// <summary>
        /// Materializa las páginas de un DataFile ya persistido (con Id asignado).
        /// Descarta páginas sin número válido y colapsa números repetidos, para no
        /// violar el índice único UQ_DataFilePage_File_Page.
        /// </summary>
        public static List<DataFilePage> Materializar(int dataFileId, IEnumerable<PaginaOcr>? paginas)
        {
            if (dataFileId <= 0 || paginas == null)
                return new List<DataFilePage>();

            var ahora = DateTime.UtcNow;

            return paginas
                .Where(p => p.PageNumber >= 1)
                .GroupBy(p => p.PageNumber)
                .Select(g => g.First())
                .OrderBy(p => p.PageNumber)
                .Select(p => new DataFilePage
                {
                    DataFileId  = dataFileId,
                    PageNumber  = p.PageNumber,
                    Text        = p.Text,
                    Width       = p.Width,
                    Height      = p.Height,
                    Unit        = p.Unit,
                    Angle       = p.Angle,
                    LineCount   = p.LineCount,
                    WordCount   = p.WordCount,
                    CharCount   = p.Text?.Length ?? 0,
                    CreatedDate = ahora
                })
                .ToList();
        }
    }
}
