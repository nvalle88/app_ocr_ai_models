using app_tramites.Models.ModelAi;

namespace app_tramites.Services.Ai;

/// <summary>
/// Factory que construye la implementación correcta de <see cref="IAiCompletionService"/>
/// según el campo <see cref="OPAIConfiguration.Provider"/> del agente.
/// </summary>
public sealed class AiCompletionServiceFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Crea el factory con el <see cref="IHttpClientFactory"/> necesario para AzureOpenAI
    /// y el <see cref="ILoggerFactory"/> para crear loggers tipados.
    /// </summary>
    /// <param name="httpClientFactory">Factory de HttpClient registrado en DI.</param>
    /// <param name="loggerFactory">Factory de loggers registrado en DI.</param>
    public AiCompletionServiceFactory(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory
            ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _loggerFactory = loggerFactory
            ?? throw new ArgumentNullException(nameof(loggerFactory));
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
        {
            var logger = _loggerFactory.CreateLogger<ClaudeCompletionService>();
            return new ClaudeCompletionService(config, logger);
        }

        return new AzureOpenAiCompletionService(config, _httpClientFactory);
    }
}
