using app_tramites.Models.ModelAi;

namespace app_tramites.Services.Ai;

/// <summary>
/// Factory que construye la implementación correcta de <see cref="IAiCompletionService"/>
/// según el campo <see cref="OPAIConfiguration.Provider"/> del agente.
/// </summary>
public sealed class AiCompletionServiceFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Crea el factory con el <see cref="IHttpClientFactory"/> necesario para AzureOpenAI.
    /// </summary>
    /// <param name="httpClientFactory">Factory de HttpClient registrado en DI.</param>
    public AiCompletionServiceFactory(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory
            ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <summary>
    /// Devuelve el servicio de completado apropiado para la configuración del agente.
    /// </summary>
    /// <param name="config">Configuración del modelo con Provider, EndpointUrl y ApiKey.</param>
    /// <returns>
    /// <see cref="ClaudeCompletionService"/> si <c>Provider == "Anthropic"</c>;
    /// <see cref="AzureOpenAiCompletionService"/> en cualquier otro caso.
    /// </returns>
    public IAiCompletionService Create(OPAIConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.Equals(config.Provider, "Anthropic", StringComparison.OrdinalIgnoreCase))
            return new ClaudeCompletionService(config);

        return new AzureOpenAiCompletionService(config, _httpClientFactory);
    }
}
