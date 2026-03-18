using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Models;

public sealed class AnthropicModelProvider : IModelProvider
{
    private readonly ModelProviderOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AnthropicModelProvider> _logger;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
    private const int MaxRetries = 3;
    private const string AnthropicVersion = "2023-06-01";

    public AnthropicModelProvider(
        IOptions<ModelProviderOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<AnthropicModelProvider> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string ProviderName => "anthropic";

    public bool CanHandle(string model) => model.StartsWith("anthropic", StringComparison.OrdinalIgnoreCase);

    public async Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_options.Anthropic.ApiKey))
        {
            return CreateError(request, "Anthropic API key is not configured.");
        }

        string apiModel = MapModelId(request.Model);
        var sw = Stopwatch.StartNew();
        int attempt = 0;

        while (true)
        {
            attempt++;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(request.Timeout ?? DefaultTimeout);

                var result = await CallApiAsync(request, apiModel, cts.Token);
                sw.Stop();
                return result with { LatencyMs = sw.Elapsed.TotalMilliseconds };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxRetries && IsRetryable(ex))
            {
                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 500);
                _logger.LogWarning(ex,
                    "Anthropic request {CorrelationId} attempt {Attempt}/{Max} failed, retrying in {Delay}ms",
                    request.CorrelationId, attempt, MaxRetries, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return CreateError(request, $"Anthropic request timed out after {sw.Elapsed.TotalMilliseconds:F0}ms") with
                {
                    LatencyMs = sw.Elapsed.TotalMilliseconds
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Anthropic request {CorrelationId} failed permanently", request.CorrelationId);
                return CreateError(request, $"Anthropic provider error: {ex.Message}") with
                {
                    LatencyMs = sw.Elapsed.TotalMilliseconds
                };
            }
        }
    }

    private async Task<ModelResponse> CallApiAsync(ModelRequest request, string apiModel, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient("Anthropic");
        client.BaseAddress = new Uri(_options.Anthropic.Endpoint.TrimEnd('/'));
        client.DefaultRequestHeaders.Add("x-api-key", _options.Anthropic.ApiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);

        var messages = new List<object>
        {
            new { role = "user", content = request.Prompt }
        };

        var body = new Dictionary<string, object>
        {
            ["model"] = apiModel,
            ["messages"] = messages,
            ["max_tokens"] = request.MaxTokens ?? 4096,
        };

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            body["system"] = request.SystemPrompt;
        if (request.Temperature.HasValue)
            body["temperature"] = request.Temperature.Value;

        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/v1/messages", content, ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = TryExtractErrorMessage(responseBody) ?? $"HTTP {(int)response.StatusCode}";
            return CreateError(request, $"Anthropic API error: {errorMessage}");
        }

        return ParseResponse(request, responseBody);
    }

    private ModelResponse ParseResponse(ModelRequest request, string responseBody)
    {
        try
        {
            var doc = JsonNode.Parse(responseBody);

            // Extract text from content blocks
            var contentBlocks = doc?["content"]?.AsArray();
            var textParts = new List<string>();
            if (contentBlocks is not null)
            {
                foreach (var block in contentBlocks)
                {
                    if (block?["type"]?.GetValue<string>() == "text")
                        textParts.Add(block["text"]?.GetValue<string>() ?? "");
                }
            }
            var text = string.Join("", textParts);
            var stopReason = doc?["stop_reason"]?.GetValue<string>();

            var usageNode = doc?["usage"];
            TokenUsage? usage = null;
            if (usageNode is not null)
            {
                var input = usageNode["input_tokens"]?.GetValue<int>() ?? 0;
                var output = usageNode["output_tokens"]?.GetValue<int>() ?? 0;
                usage = new TokenUsage(input, output, input + output);
            }

            var warnings = new List<string>();
            if (stopReason == "max_tokens")
                warnings.Add("Response truncated: max tokens reached.");

            return new ModelResponse(
                Provider: ProviderName,
                Model: request.Model,
                IsSuccess: true,
                Content: text,
                Warnings: warnings,
                Errors: Array.Empty<string>(),
                CompletedAtUtc: DateTimeOffset.UtcNow)
            {
                CorrelationId = request.CorrelationId,
                Usage = usage,
                FinishReason = stopReason,
            };
        }
        catch (Exception ex)
        {
            return CreateError(request, $"Failed to parse Anthropic response: {ex.Message}");
        }
    }

    private static string MapModelId(string logicalModel)
    {
        var dotIndex = logicalModel.IndexOf('.');
        return dotIndex >= 0 ? logicalModel[(dotIndex + 1)..] : logicalModel;
    }

    private static bool IsRetryable(Exception ex) =>
        ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests
            or System.Net.HttpStatusCode.InternalServerError
            or System.Net.HttpStatusCode.BadGateway
            or System.Net.HttpStatusCode.ServiceUnavailable
            or System.Net.HttpStatusCode.GatewayTimeout }
        || ex is TaskCanceledException;

    private static string? TryExtractErrorMessage(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            return node?["error"]?["message"]?.GetValue<string>();
        }
        catch (JsonException)
        {
            // API returned non-JSON error body — caller will use HTTP status code instead
            return null;
        }
    }

    private ModelResponse CreateError(ModelRequest request, string message) =>
        new(Provider: ProviderName,
            Model: request.Model,
            IsSuccess: false,
            Content: string.Empty,
            Warnings: Array.Empty<string>(),
            Errors: new[] { message },
            CompletedAtUtc: DateTimeOffset.UtcNow)
        {
            CorrelationId = request.CorrelationId,
        };
}
