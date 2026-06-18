namespace app_ocr_ai_models.Services.Zendesk;

// ============================================================
// REQ-019 T3 — Contrato del cliente Zendesk propio de Nexus.
// ============================================================

/// <summary>
/// Cliente Zendesk multi-cuenta para el área nueva de Nexus.
/// Expone búsqueda de sobres/tickets, lectura, comentarios/auditoría y
/// descarga binaria de adjuntos. No toca ni depende de api-comunicacion.
/// </summary>
public interface IZendeskClient
{
    // ----------------------------------------------------------------
    // Búsqueda
    // ----------------------------------------------------------------

    /// <summary>
    /// Busca tickets que tengan el número de sobre indicado
    /// (custom field 360029154411) en las cuentas activas.
    /// </summary>
    /// <param name="numeroSobre">Valor del campo NumeroSobre.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>
    /// Lista de resultados agrupados por cuenta (puede devolver
    /// resultados de más de una cuenta si el sobre aparece en varias).
    /// </returns>
    Task<IReadOnlyList<ZendeskBusquedaResultDto>> BuscarPorNumeroSobreAsync(
        string numeroSobre,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca tickets asociados a una persona mediante su cédula
    /// (custom field 360022133372) en las cuentas activas.
    /// </summary>
    /// <param name="cedula">Número de cédula del beneficiario o titular.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<IReadOnlyList<ZendeskBusquedaResultDto>> BuscarPorPersonaAsync(
        string cedula,
        CancellationToken cancellationToken = default);

    // ----------------------------------------------------------------
    // Lectura de ticket
    // ----------------------------------------------------------------

    /// <summary>
    /// Obtiene el detalle completo de un ticket por su ID en la cuenta indicada.
    /// </summary>
    /// <param name="ticketId">ID numérico del ticket en Zendesk.</param>
    /// <param name="cuenta">Cuenta donde reside el ticket.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>DTO con datos del ticket, o <c>null</c> si no existe (404).</returns>
    Task<ZendeskTicketDto?> LeerTicketAsync(
        long ticketId,
        ZendeskCuenta cuenta,
        CancellationToken cancellationToken = default);

    // ----------------------------------------------------------------
    // Comentarios / auditoría
    // ----------------------------------------------------------------

    /// <summary>
    /// Obtiene todos los comentarios (incluyendo la auditoría) de un ticket.
    /// </summary>
    /// <param name="ticketId">ID numérico del ticket.</param>
    /// <param name="cuenta">Cuenta donde reside el ticket.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<IReadOnlyList<ZendeskComentarioDto>> ObtenerComentariosAsync(
        long ticketId,
        ZendeskCuenta cuenta,
        CancellationToken cancellationToken = default);

    // ----------------------------------------------------------------
    // Descarga de adjuntos (capacidad NUEVA — no existe en api-comunicacion)
    // ----------------------------------------------------------------

    /// <summary>
    /// Descarga el contenido binario de un adjunto dado su content_url.
    /// El <paramref name="contentUrl"/> proviene de
    /// <see cref="ZendeskAdjuntoDto.ContentUrl"/>.
    /// El llamador es responsable de disponer el stream devuelto.
    /// </summary>
    /// <param name="contentUrl">URL de descarga devuelta por la API de Zendesk.</param>
    /// <param name="cuenta">Cuenta cuyo Bearer token se usa para autenticar.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Stream con el contenido binario del adjunto.</returns>
    Task<Stream> DescargarAdjuntoAsync(
        string contentUrl,
        ZendeskCuenta cuenta,
        CancellationToken cancellationToken = default);

    // ----------------------------------------------------------------
    // Utilidades de configuración
    // ----------------------------------------------------------------

    /// <summary>
    /// Determina la cuenta destino a partir del brand_id de un ticket.
    /// Retorna <c>null</c> si el brand_id no corresponde a ninguna cuenta conocida.
    /// </summary>
    /// <param name="brandId">Brand ID obtenido del ticket.</param>
    ZendeskCuenta? ResolverCuentaPorBrandId(long brandId);

    /// <summary>
    /// Lista las cuentas activas disponibles según la configuración en BD.
    /// </summary>
    IReadOnlyList<ZendeskCuentaConfig> CuentasActivas { get; }
}
