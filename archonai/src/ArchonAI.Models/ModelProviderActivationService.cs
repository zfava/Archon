using System.Diagnostics.Metrics;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Models;

/// <summary>
/// Hosted service that validates model provider configuration on startup.
/// Enumerates all providers, tests key presence, logs a status table,
/// emits Prometheus-compatible metrics, and publishes an activation event.
/// </summary>
public sealed class ModelProviderActivationService : IHostedService
{
    private readonly ModelProviderOptions _options;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ModelProviderActivationService> _logger;

    private static readonly Meter Meter = new("ArchonAI.Models", "1.0.0");
    private static readonly Dictionary<string, int> ProviderGaugeValues = new();

    private static readonly ObservableGauge<int> ActiveProvidersGauge =
        Meter.CreateObservableGauge(
            "archonai_model_providers_active",
            () => ProviderGaugeValues.Select(kv =>
                new Measurement<int>(kv.Value, new KeyValuePair<string, object?>("provider", kv.Key))),
            description: "Whether a model provider is active (1) or inactive (0)");

    public ModelProviderActivationService(
        IOptions<ModelProviderOptions> options,
        IEventBus eventBus,
        ILogger<ModelProviderActivationService> logger)
    {
        _options = options.Value;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async global::System.Threading.Tasks.Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ModelProviderActivationService: Validating model provider configuration...");

        var statuses = new Dictionary<string, ProviderStatus>();

        // OpenAI
        statuses["OpenAI"] = EvaluateProvider(
            "OpenAI",
            _options.OpenAI.Enabled,
            _options.OpenAI.ApiKey,
            requiresApiKey: true);

        // Anthropic
        statuses["Anthropic"] = EvaluateProvider(
            "Anthropic",
            _options.Anthropic.Enabled,
            _options.Anthropic.ApiKey,
            requiresApiKey: true);

        // AzureOpenAI
        statuses["AzureOpenAI"] = EvaluateProvider(
            "AzureOpenAI",
            _options.AzureOpenAI.Enabled,
            _options.AzureOpenAI.ApiKey,
            requiresApiKey: true,
            additionalCheck: () => !string.IsNullOrWhiteSpace(_options.AzureOpenAI.Endpoint),
            additionalFailReason: "Endpoint not configured");

        // Local (no API key required)
        statuses["Local"] = EvaluateProvider(
            "Local",
            _options.Local.Enabled,
            apiKey: null,
            requiresApiKey: false);

        // Set gauge values
        foreach (var (provider, status) in statuses)
            ProviderGaugeValues[provider] = status.IsActive ? 1 : 0;

        // Log status table
        _logger.LogInformation("╔══════════════════════════════════════════════════════════╗");
        _logger.LogInformation("║           MODEL PROVIDER ACTIVATION STATUS               ║");
        _logger.LogInformation("╠══════════════════╦══════════╦════════════════════════════╣");
        _logger.LogInformation("║ Provider         ║ Status   ║ Reason                     ║");
        _logger.LogInformation("╠══════════════════╬══════════╬════════════════════════════╣");
        foreach (var (provider, status) in statuses)
        {
            var statusLabel = status.IsActive ? "ACTIVE" : "INACTIVE";
            _logger.LogInformation("║ {Provider,-16} ║ {Status,-8} ║ {Reason,-26} ║",
                provider, statusLabel, status.Reason);
        }
        _logger.LogInformation("╚══════════════════╩══════════╩════════════════════════════╝");

        int activeCount = statuses.Count(kv => kv.Value.IsActive);

        if (activeCount == 0)
        {
            _logger.LogCritical(
                "ZERO model providers are active. AI endpoints will return errors. " +
                "Configure at least one provider API key via environment variables or appsettings.json. " +
                "Non-AI endpoints remain functional.");
        }
        else
        {
            _logger.LogInformation("Model provider activation complete: {ActiveCount}/{TotalCount} providers active",
                activeCount, statuses.Count);
        }

        // Publish activation event
        var payload = statuses.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.IsActive ? "active" : $"inactive:{kv.Value.Reason}");

        payload["_activeCount"] = activeCount.ToString();
        payload["_totalCount"] = statuses.Count.ToString();

        await _eventBus.PublishAsync(new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: "model.providers.activated",
            Source: nameof(ModelProviderActivationService),
            CorrelationId: Guid.NewGuid(),
            Payload: payload,
            OccurredAtUtc: DateTimeOffset.UtcNow
        ), cancellationToken);
    }

    public global::System.Threading.Tasks.Task StopAsync(CancellationToken cancellationToken) =>
        global::System.Threading.Tasks.Task.CompletedTask;

    private static ProviderStatus EvaluateProvider(
        string name,
        bool enabled,
        string? apiKey,
        bool requiresApiKey,
        Func<bool>? additionalCheck = null,
        string? additionalFailReason = null)
    {
        if (!enabled)
            return new ProviderStatus(false, "Disabled in configuration");

        if (requiresApiKey && string.IsNullOrWhiteSpace(apiKey))
            return new ProviderStatus(false, "API key not configured");

        if (additionalCheck is not null && !additionalCheck())
            return new ProviderStatus(false, additionalFailReason ?? "Additional check failed");

        return new ProviderStatus(true, "Ready");
    }

    private sealed record ProviderStatus(bool IsActive, string Reason);
}
