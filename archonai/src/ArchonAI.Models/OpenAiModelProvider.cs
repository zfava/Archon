using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Models;

public sealed class OpenAiModelProvider : IModelProvider
{
    private readonly ModelProviderOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenAiModelProvider> _logger;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
    private const int MaxRetries = 3;

    public OpenAiModelProvider(
        IOptions<ModelProviderOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<OpenAiModelProvider> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string ProviderName => "openai";

    public bool CanHandle(string model) => model.StartsWith("openai", StringComparison.OrdinalIgnoreCase);

    public async Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_options.OpenAI.ApiKey))
        {
            return CreateError(request, "OpenAI API key is not configured.");
        }

        // Map logical model name to API model ID
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
                throw; // Caller cancelled — propagate
            }
            catch (Exception ex) when (attempt < MaxRetries && IsRetryable(ex))
            {
                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 500);
                _logger.LogWarning(ex,
                    "OpenAI request {CorrelationId} attempt {Attempt}/{Max} failed, retrying in {Delay}ms",
                    request.CorrelationId, attempt, MaxRetries, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return CreateError(request, $"OpenAI request timed out after {sw.Elapsed.TotalMilliseconds:F0}ms") with
                {
                    LatencyMs = sw.Elapsed.TotalMilliseconds
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "OpenAI request {CorrelationId} failed permanently", request.CorrelationId);
                return CreateError(request, $"OpenAI provider error: {ex.Message}") with
                {
                    LatencyMs = sw.Elapsed.TotalMilliseconds
                };
            }
        }
    }

    private async Task<ModelResponse> CallApiAsync(ModelRequest request, string apiModel, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient("OpenAI");
        client.BaseAddress = new Uri(_options.OpenAI.Endpoint.TrimEnd('/'));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenAI.ApiKey);

        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            messages.Add(new { role = "system", content = request.SystemPrompt });
        messages.Add(new { role = "user", content = request.Prompt });

        var body = new Dictionary<string, object>
        {
            ["model"] = apiModel,
            ["messages"] = messages,
        };

        if (request.MaxTokens.HasValue)
            body["max_output_tokens"] = request.MaxTokens.Value;
        if (request.Temperature.HasValue)
            body["temperature"] = request.Temperature.Value;
        if (request.ResponseJsonSchema is not null)
        {
            body["response_format"] = new { type = "json_object" };
        }

        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/v1/chat/completions", content, ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = TryExtractErrorMessage(responseBody) ?? $"HTTP {(int)response.StatusCode}";
            return CreateError(request, $"OpenAI API error: {errorMessage}");
        }

        return ParseResponse(request, responseBody);
    }

    private ModelResponse ParseResponse(ModelRequest request, string responseBody)
    {
        try
        {
            var doc = JsonNode.Parse(responseBody);
            var choice = doc?["choices"]?[0];
            var text = choice?["message"]?["content"]?.GetValue<string>() ?? string.Empty;
            var finishReason = choice?["finish_reason"]?.GetValue<string>();

            var usageNode = doc?["usage"];
            TokenUsage? usage = null;
            if (usageNode is not null)
            {
                var prompt = usageNode["prompt_tokens"]?.GetValue<int>() ?? 0;
                var completion = usageNode["completion_tokens"]?.GetValue<int>() ?? 0;
                usage = new TokenUsage(prompt, completion, prompt + completion);
            }

            var warnings = new List<string>();
            if (finishReason == "length")
                warnings.Add("Response truncated: max tokens reached.");
            if (finishReason == "content_filter")
                warnings.Add("Response filtered by content policy.");

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
                FinishReason = finishReason,
            };
        }
        catch (Exception ex)
        {
            return CreateError(request, $"Failed to parse OpenAI response: {ex.Message}");
        }
    }

    private static string MapModelId(string logicalModel)
    {
        // Strip provider prefix: "openai.gpt-4.1" → "gpt-4.1"
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
        catch { return null; }
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
