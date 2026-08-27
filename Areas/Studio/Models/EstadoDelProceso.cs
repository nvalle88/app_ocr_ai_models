using System;
using System.Collections.Generic;
using System.Linq;

namespace app_ocr_ai_models.Areas.Studio.Models;

// =============================================================================
// Cuándo un paso del portal está HECHO.
//
// Vive aquí y no dentro del controlador porque esta decisión se ha equivocado
// tres veces seguidas, siempre por la misma confusión, y cada vez el afiliado
// se comió el fallo entero:
//
//   1. La resolución contaba como hecha porque EXISTÍA la nota — aunque la nota
//      fuese {"_raw":true}, trece caracteres que significan «no salió nada».
//      El botón «Calcular» recorría una lista vacía y recargaba: «no hace nada».
//   2. La tipificación contaba como hecha porque había UNA clasificación —
//      aunque quedaran documentos sin leer. Subiendo en dos tandas, los de la
//      segunda se quedaban «Identificando el documento…» para siempre.
//   3. El expediente contaba como hecho porque su nota era posterior al último
//      documento — aunque se hubiera construido con la mitad de los papeles sin
//      clasificar.
//
// Los tres son la misma equivocación: confundir que ALGO ocurrió con que el
// trabajo esté TERMINADO. Con un expediente que crece mientras el afiliado
// adjunta, la pregunta nunca es «¿hay algo?», es «¿falta algo?».
// =============================================================================
public static class EstadoDelProceso
{
    /// <summary>
    /// La tipificación está hecha cuando lo está para TODOS los documentos.
    /// Sin documentos no está hecha: no hay nada que dar por bueno.
    /// </summary>
    public static bool TodosTipificados(IEnumerable<int> documentos,
                                        IEnumerable<int> yaTipificados)
    {
        var docs = documentos?.ToList() ?? new List<int>();
        if (docs.Count == 0) return false;

        var hechos = new HashSet<int>(yaTipificados ?? Enumerable.Empty<int>());
        return docs.All(hechos.Contains);
    }

    /// <summary>
    /// Una nota vale mientras no hayan llegado documentos después de ella.
    /// Si el afiliado adjunta la factura que le faltaba, el expediente y la
    /// resolución anteriores hablan de un conjunto de papeles que ya no es el
    /// suyo: darlos por buenos le devolvería el mismo resultado, con el
    /// documento nuevo ignorado y sin una sola señal de que no se tuvo en cuenta.
    ///
    /// Las dos fechas son UTC (el servidor SQL corre en UTC y ambas columnas se
    /// escriben con DateTime.UtcNow), así que se comparan directamente.
    /// </summary>
    public static bool NotaVigente(DateTime notaCreada, DateTime? ultimoDocumento) =>
        ultimoDocumento is null || notaCreada >= ultimoDocumento.Value;

    /// <summary>
    /// Un paso no puede estar hecho si el anterior no lo está: es el invariante
    /// de una cadena. En cuanto uno queda pendiente, todo lo que viene detrás
    /// vuelve a pendiente y se recalcula en orden.
    /// </summary>
    public static void PropagarPendientes(IList<PasoClienteVm> pasos)
    {
        if (pasos is null) return;
        for (var i = 1; i < pasos.Count; i++)
            if (!pasos[i - 1].Hecho) pasos[i].Hecho = false;
    }
}
