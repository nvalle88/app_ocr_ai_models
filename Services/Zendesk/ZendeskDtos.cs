using System.Text.Json.Serialization;

namespace app_ocr_ai_models.Services.Zendesk;

// ============================================================
// REQ-019 T3 — DTOs propios para el cliente Zendesk de Nexus.
// No reutilizan tipos de api-comunicacion.
// ============================================================

/// <summary>Cuenta Zendesk disponible para selección multi-cuenta.</summary>
public enum ZendeskCuenta
{
    /// <summary>Auxiliar (brand_id 1692030147).</summary>
    Auxiliar,

    /// <summary>Digital (brand_id 18509808293645).</summary>
    Digital,

    /// <summary>Experience.</summary>
    Experience
}

// ----------------------------------------------------------------
// Configuración de cuenta (se hidrata desde ZendeskConf en BD)
// ----------------------------------------------------------------

/// <summary>Parámetros de conexión resueltos para una cuenta Zendesk.</summary>
public sealed class ZendeskCuentaConfig
{
    /// <summary>Código de cuenta (ej. "Auxiliar").</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Sub-dominio Zendesk, ej. "saludsa".</summary>
    public string Subdomain { get; init; } = string.Empty;

    /// <summary>URL base: https://{Subdomain}.zendesk.com/</summary>
    public string BaseUrl => $"https://{Subdomain}.zendesk.com/";

    /// <summary>Token Bearer ya resuelto (desde Key Vault / config).</summary>
    public string Token { get; init; } = string.Empty;
}

// ----------------------------------------------------------------
// Ticket / sobre
// ----------------------------------------------------------------

/// <summary>Resumen de un sobre/ticket Zendesk.</summary>
public sealed class ZendeskSobreDto
{
    /// <summary>ID numérico del ticket en Zendesk.</summary>
    public long Id { get; init; }

    /// <summary>Asunto del ticket.</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>Estado del ticket.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Número de sobre (custom field 360029154411).</summary>
    public string? NumeroSobre { get; init; }

    /// <summary>Cédula del beneficiario (custom field 360022133372).</summary>
    public string? CedulaBeneficiario { get; init; }

    /// <summary>Brand ID del ticket, identifica la cuenta.</summary>
    public long BrandId { get; init; }

    /// <summary>Cuenta determinada por el selector multi-cuenta.</summary>
    public ZendeskCuenta Cuenta { get; init; }

    /// <summary>Fecha de creación UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Fecha de última actualización UTC.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Detalle completo de un ticket Zendesk.</summary>
public sealed class ZendeskTicketDto
{
    /// <summary>ID numérico del ticket.</summary>
    public long Id { get; init; }

    /// <summary>Asunto.</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>Estado.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Prioridad.</summary>
    public string? Priority { get; init; }

    /// <summary>Cuenta a la que pertenece.</summary>
    public ZendeskCuenta Cuenta { get; init; }

    /// <summary>Brand ID original.</summary>
    public long BrandId { get; init; }

    /// <summary>Número de sobre (custom field 360029154411).</summary>
    public string? NumeroSobre { get; init; }

    /// <summary>Cédula del beneficiario (custom field 360022133372).</summary>
    public string? CedulaBeneficiario { get; init; }

    /// <summary>Campos personalizados completos (id → value).</summary>
    public IReadOnlyDictionary<long, string?> CustomFields { get; init; }
        = new Dictionary<long, string?>();

    /// <summary>Fecha de creación UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Fecha de última actualización UTC.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

// ----------------------------------------------------------------
// Comentario / auditoría
// ----------------------------------------------------------------

/// <summary>Comentario (auditoría) de un ticket Zendesk.</summary>
public sealed class ZendeskComentarioDto
{
    /// <summary>ID del comentario.</summary>
    public long Id { get; init; }

    /// <summary>Cuerpo del comentario en texto plano.</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>Indica si el comentario es público.</summary>
    public bool IsPublic { get; init; }

    /// <summary>ID del autor.</summary>
    public long AuthorId { get; init; }

    /// <summary>Fecha de creación UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Adjuntos del comentario.</summary>
    public IReadOnlyList<ZendeskAdjuntoDto> Attachments { get; init; }
        = Array.Empty<ZendeskAdjuntoDto>();
}

// ----------------------------------------------------------------
// Adjunto
// ----------------------------------------------------------------

/// <summary>Metadatos de un adjunto de Zendesk.</summary>
public sealed class ZendeskAdjuntoDto
{
    /// <summary>ID del adjunto.</summary>
    public long Id { get; init; }

    /// <summary>Nombre de archivo.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>MIME type.</summary>
    public string ContentType { get; init; } = string.Empty;

    /// <summary>Tamaño en bytes.</summary>
    public long Size { get; init; }

    /// <summary>URL de descarga (content_url en la API de Zendesk).</summary>
    public string ContentUrl { get; init; } = string.Empty;
}

// ----------------------------------------------------------------
// Resultado de búsqueda
// ----------------------------------------------------------------

/// <summary>Resultado paginado de la Search API de Zendesk por cuenta.</summary>
public sealed class ZendeskBusquedaResultDto
{
    /// <summary>Tickets encontrados.</summary>
    public IReadOnlyList<ZendeskSobreDto> Items { get; init; }
        = Array.Empty<ZendeskSobreDto>();

    /// <summary>Cuenta en la que se realizó la búsqueda.</summary>
    public ZendeskCuenta Cuenta { get; init; }

    /// <summary>Total de resultados reportado por Zendesk (puede ser estimado).</summary>
    public int TotalCount { get; init; }
}

// ----------------------------------------------------------------
// Modelos internos para deserialización de la API de Zendesk
// (se usan solo dentro del cliente; no son parte de la interfaz pública)
// ----------------------------------------------------------------

internal sealed class ZdApiTicketEnvelope
{
    [JsonPropertyName("ticket")]
    public ZdApiTicket? Ticket { get; init; }
}

internal sealed class ZdApiSearchResponse
{
    [JsonPropertyName("results")]
    public List<ZdApiTicket>? Results { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("next_page")]
    public string? NextPage { get; init; }
}

internal sealed class ZdApiTicket
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("subject")]
    public string Subject { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("priority")]
    public string? Priority { get; init; }

    [JsonPropertyName("brand_id")]
    public long BrandId { get; init; }

    [JsonPropertyName("custom_fields")]
    public List<ZdApiCustomField>? CustomFields { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; init; }
}

internal sealed class ZdApiCustomField
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }
}

internal sealed class ZdApiCommentsEnvelope
{
    [JsonPropertyName("comments")]
    public List<ZdApiComment>? Comments { get; init; }
}

internal sealed class ZdApiComment
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    [JsonPropertyName("public")]
    public bool IsPublic { get; init; }

    [JsonPropertyName("author_id")]
    public long AuthorId { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("attachments")]
    public List<ZdApiAttachment>? Attachments { get; init; }
}

internal sealed class ZdApiAttachment
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("file_name")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("content_type")]
    public string ContentType { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("content_url")]
    public string ContentUrl { get; init; } = string.Empty;
}
