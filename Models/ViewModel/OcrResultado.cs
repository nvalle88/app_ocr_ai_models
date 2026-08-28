using System.Collections.Generic;

namespace app_tramites.Models.ViewModel
{
    /// <summary>
    /// Texto OCR de UNA página del documento (proyección de Azure.AI.DocumentIntelligence.DocumentPage).
    /// </summary>
    public class PaginaOcr
    {
        /// <summary>Número de página 1-based, tal como lo entrega DocumentPage.PageNumber.</summary>
        public int PageNumber { get; set; }

        /// <summary>Texto de la página reconstruido a partir de sus DocumentLine.</summary>
        public string Text { get; set; } = string.Empty;

        public float? Width { get; set; }

        public float? Height { get; set; }

        /// <summary>'pixel' (imagen) o 'inch' (PDF).</summary>
        public string? Unit { get; set; }

        /// <summary>Orientación del contenido en grados, (-180, 180].</summary>
        public float? Angle { get; set; }

        public int LineCount { get; set; }

        public int WordCount { get; set; }

        /// <summary>true si el texto se obtuvo por el fallback de spans (calidad menos garantizada).</summary>
        public bool TextoDesdeSpans { get; set; }
    }

    /// <summary>
    /// Resultado DETALLADO de la ingesta: lo mismo que devolvía la tupla
    /// (Url, Text) más el desglose por página.
    /// </summary>
    public class OcrResultado
    {
        /// <summary>URL pública del blob subido.</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Texto COMPLETO del documento. Es literalmente AnalyzeResult.Content:
        /// idéntico byte a byte a lo que ProcessFileAsync devolvía antes.
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Páginas OCR. Lista VACÍA (nunca null) si el modelo no devolvió Pages.</summary>
        public List<PaginaOcr> Paginas { get; set; } = new List<PaginaOcr>();
    }
}
