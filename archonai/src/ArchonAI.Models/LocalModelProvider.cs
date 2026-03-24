using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Models;

/// <summary>
/// Local model provider that calls an Ollama-compatible API at the configured endpoint.
/// Fails explicitly when the local server is unreachable — no echo or simulated output.
/// </summary>
public sealed class LocalModelProvider : IModelProvider
{
    private readonly ModelProviderOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LocalModelProvider> _logger;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    public LocalModelProvider(
        IOptions<ModelProviderOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<LocalModelProvider> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string ProviderName => "local";

    public bool CanHandle(string model)
    {
        return model.StartsWith("local", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("ollama", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("gguf", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string modelName = string.IsNullOrWhiteSpace(request.Model)
            ? _options.Local.DefaultModel
            : request.Model;

        var sw = Stopwatch.StartNew();

        // Attempt real Ollama API call
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(request.Timeout ?? DefaultTimeout);

            var result = await CallOllamaApiAsync(request, modelName, cts.Token);
            sw.Stop();
            return result with { LatencyMs = sw.Elapsed.TotalMilliseconds };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Local model server unreachable at {Endpoint} for {CorrelationId}. No fallback — failing explicitly.",
                _options.Local.Endpoint, request.CorrelationId);

            return new ModelResponse(
                Provider: ProviderName,
                Model: modelName,
                IsSuccess: false,
                Content: string.Empty,
                Warnings: Array.Empty<string>(),
                Errors: new[] { $"Local model server unavailable at {_options.Local.Endpoint}. Configure a cloud provider (OpenAI/Anthropic) or start the local Ollama server." },
                CompletedAtUtc: DateTimeOffset.UtcNow)
            {
                CorrelationId = request.CorrelationId,
                FinishReason = "provider_unavailable",
                LatencyMs = sw.Elapsed.TotalMilliseconds,
            };
        }
    }

    private async Task<ModelResponse> CallOllamaApiAsync(ModelRequest request, string modelName, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient("Local");
        client.BaseAddress = new Uri(_options.Local.Endpoint.TrimEnd('/'));

        // Strip prefix: "local.llama3" → "llama3"
        var ollamaModel = modelName;
        var dotIndex = ollamaModel.IndexOf('.');
        if (dotIndex >= 0) ollamaModel = ollamaModel[(dotIndex + 1)..];

        var body = new Dictionary<string, object>
        {
            ["model"] = ollamaModel,
            ["prompt"] = request.Prompt,
            ["stream"] = false,
        };

        if (request.MaxTokens.HasValue)
            body["options"] = new { num_predict = request.MaxTokens.Value };

        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/generate", content, ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();

        var doc = JsonNode.Parse(responseBody);
        var text = doc?["response"]?.GetValue<string>() ?? string.Empty;

        int promptTokens = doc?["prompt_eval_count"]?.GetValue<int>() ?? 0;
        int completionTokens = doc?["eval_count"]?.GetValue<int>() ?? 0;

        return new ModelResponse(
            Provider: ProviderName,
            Model: modelName,
            IsSuccess: true,
            Content: text,
            Warnings: Array.Empty<string>(),
            Errors: Array.Empty<string>(),
            CompletedAtUtc: DateTimeOffset.UtcNow)
        {
            CorrelationId = request.CorrelationId,
            Usage = new TokenUsage(promptTokens, completionTokens, promptTokens + completionTokens),
            FinishReason = "stop",
        };
    }
}
