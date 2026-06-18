using app_tramites.Models.ModelAi;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace app_tramites.Services.Ai;

/// <summary>
/// Implementación de <see cref="IAiCompletionService"/> para Azure OpenAI.
/// Reutiliza el patrón HTTP del flujo legacy (NexusService.CallOpenAiAsync)
/// sin tocarlo; opera sobre la configuración del agente recibida.
/// </summary>
public sealed class AzureOpenAiCompletionService : IAiCompletionService
{
    private readonly OPAIConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Crea una instancia para la configuración de modelo indicada.
    /// </summary>
    /// <param name="config">Configuración del modelo (EndpointUrl + ApiKey).</param>
    /// <param name="httpClientFactory">Factory de HttpClient (evita socket exhaustion).</param>
    public AzureOpenAiCompletionService(
        OPAIConfiguration config,
        IHttpClientFactory httpClientFactory)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <inheritdoc />
    public async Task<AiCompletionResult> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = new
        {
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user",   content = request.UserMessage  }
            },
            max_tokens  = request.MaxTokens,
            temperature = (double)(request.Temperature ?? 0.2m)
        };

        var json    = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("api-key", _config.ApiKey);

        var response = await client.PostAsync(_config.EndpointUrl, content, cancellationToken);
        var resultJson = await response.Content.ReadAsStringAsync(cancellationToken);

        var ai = JsonSerializer.Deserialize<AzureOpenAiResponse>(
            resultJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var text = ai?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;

        return new AiCompletionResult
        {
            Text             = text.Trim(),
            PromptTokens     = ai?.Usage?.PromptTokens     ?? 0,
            CompletionTokens = ai?.Usage?.CompletionTokens ?? 0
        };
    }

    // ── DTOs internos para deserializar la respuesta de Azure OpenAI ──

    private sealed class AzureOpenAiResponse
    {
        [JsonPropertyName("choices")]
        public List<AzureOpenAiChoice>? Choices { get; set; }

        [JsonPropertyName("usage")]
        public AzureOpenAiUsage? Usage { get; set; }
    }

    private sealed class AzureOpenAiChoice
    {
        [JsonPropertyName("message")]
        public AzureOpenAiMessage? Message { get; set; }
    }

    private sealed class AzureOpenAiMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private sealed class AzureOpenAiUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }
    }
}
