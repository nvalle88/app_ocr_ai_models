using System.Text.Json;

namespace app_ocr_ai_models.Areas.Studio.Models;

/// <summary>
/// Datos que hay que sacar del contrato guardado (SolicitudCliente.ContratoJson).
///
/// Vive aquí porque lo usan el portal y el chat del afiliado, y tenerlo dos veces
/// era garantizar que un día divergieran: la versión del plan decide qué
/// porcentaje se aplica, así que leerla distinto en dos sitios es leer dos
/// coberturas distintas para la misma persona.
/// </summary>
public static class ContratoLeido
{
    /// <summary>
    /// La versión del plan. Sin ella no se puede consultar la cobertura: el mismo
    /// plan cambia de porcentajes entre versiones.
    /// </summary>
    public static int? Version(string? contratoJson)
    {
        if (string.IsNullOrWhiteSpace(contratoJson)) return null;
        try
        {
            var raiz = JsonDocument.Parse(contratoJson).RootElement;
            if (raiz.TryGetProperty("Version", out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetInt32();
        }
        catch { /* contrato ilegible: sin versión, y quien consulte lo dirá */ }
        return null;
    }
}
