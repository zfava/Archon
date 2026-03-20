using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Models;

public sealed class CompositeModelProvider : IModelProvider
{
    private readonly IReadOnlyList<IModelProvider> _providers;
    private readonly ModelProviderOptions _options;
    private readonly IModelRouter _modelRouter;
    private readonly IModelPerformanceTracker _performanceTracker;
    private readonly ILogger<CompositeModelProvider> _logger;

    public CompositeModelProvider(
        IOptions<ModelProviderOptions> options,
        OpenAiModelProvider openAi,
        AzureOpenAiModelProvider azureOpenAi,
        AnthropicModelProvider anthropic,
        LocalModelProvider local,
        IModelRouter modelRouter,
        IModelPerformanceTracker performanceTracker,
        ILogger<CompositeModelProvider> logger)
    {
        _options = options.Value;
        _providers = new IModelProvider[] { openAi, azureOpenAi, anthropic, local };
        _modelRouter = modelRouter;
        _performanceTracker = performanceTracker;
        _logger = logger;
    }

    public string ProviderName => "composite";

    public bool CanHandle(string model) => _providers.Any(provider => provider.CanHandle(model));

    public async Task<ModelResponse> GenerateAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var route = _modelRouter.Route(request);
        string model = string.IsNullOrWhiteSpace(route.Model) ? _options.DefaultModel : route.Model;

        var normalizedRequest = request with
        {
            Model = model,
            Parameters = new Dictionary<string, string>(request.Parameters)
            {
                ["routedProvider"] = route.Provider,
                ["routingReason"] = route.Reason,
                ["costOptimized"] = route.CostOptimized.ToString(),
                ["latencyOptimized"] = route.LatencyOptimized.ToString()
            }
        };

        IModelProvider provider = _providers.FirstOrDefault(p => p.ProviderName.Equals(route.Provider, StringComparison.OrdinalIgnoreCase) && p.CanHandle(model))
            ?? _providers.FirstOrDefault(p => p.CanHandle(model))
            ?? _providers.First(p => p is LocalModelProvider);

        _logger.LogInformation(
            "Routing request {CorrelationId} to {Provider}/{Model} (reason: {Reason})",
            request.CorrelationId, provider.ProviderName, model, route.Reason);

        var response = await provider.GenerateAsync(normalizedRequest, cancellationToken);

        // Emit structured telemetry for every model invocation
        var tags = new KeyValuePair<string, object?>[]
        {
            new("provider", provider.ProviderName),
            new("model", model),
            new("success", response.IsSuccess),
        };
        Telemetry.ModelInvocationsTotal.Add(1, tags);
        if (!response.IsSuccess)
        {
            Telemetry.ModelInvocationsFailed.Add(1, tags);
            ModelProviderHealthCheck.RecordFailure(provider.ProviderName);
        }
        else
        {
            ModelProviderHealthCheck.RecordSuccess(provider.ProviderName);
        }
        if (response.LatencyMs.HasValue)
            Telemetry.ModelInvocationDurationMs.Record(response.LatencyMs.Value, tags);
        if (response.Usage is not null)
        {
            Telemetry.ModelInvocationTokensInput.Record(response.Usage.PromptTokens, tags);
            Telemetry.ModelInvocationTokensOutput.Record(response.Usage.CompletionTokens, tags);
            Telemetry.ModelInvocationTokensTotal.Add(response.Usage.TotalTokens, tags);
        }

        // Record outcome for adaptive routing
        var taskType = request.Parameters.TryGetValue("taskType", out var tt) ? tt : null;
        if (taskType is not null)
        {
            _performanceTracker.RecordOutcome(
                provider.ProviderName, model, taskType,
                response.IsSuccess,
                response.LatencyMs ?? 0,
                EstimateCost(model, response.Usage),
                accuracy: null);
        }
        else
        {
            _performanceTracker.RecordOutcome(
                provider.ProviderName, model,
                response.IsSuccess,
                response.LatencyMs ?? 0,
                EstimateCost(model, response.Usage),
                accuracy: null);
        }

        if (!response.IsSuccess)
        {
            _logger.LogWarning(
                "Provider {Provider} returned failure for {CorrelationId}: {Errors}",
                provider.ProviderName, request.CorrelationId, string.Join("; ", response.Errors));
        }
        else
        {
            _logger.LogInformation(
                "Provider {Provider}/{Model} completed {CorrelationId} in {LatencyMs:F0}ms " +
                "(tokens: {PromptTokens}→{CompletionTokens}, finish: {FinishReason})",
                provider.ProviderName, model, request.CorrelationId,
                response.LatencyMs ?? 0,
                response.Usage?.PromptTokens ?? 0,
                response.Usage?.CompletionTokens ?? 0,
                response.FinishReason ?? "unknown");
        }

        // Guard: detect any residual echo/stub patterns in production responses
        if (response.IsSuccess && response.FinishReason == "echo_fallback")
        {
            _logger.LogCritical(
                "ECHO DETECTED: Provider {Provider} returned echo_fallback for {CorrelationId}. " +
                "This response is NOT AI-generated and should not reach production consumers.",
                provider.ProviderName, request.CorrelationId);
        }

        return response;
    }

    private static double EstimateCost(string model, TokenUsage? usage)
    {
        if (usage is null) return 0;
        var cap = ModelCapabilityRegistry.Get(model);
        if (cap is null) return 0;
        return (double)(usage.PromptTokens * cap.CostPerInputToken
            + usage.CompletionTokens * cap.CostPerOutputToken);
    }
}
