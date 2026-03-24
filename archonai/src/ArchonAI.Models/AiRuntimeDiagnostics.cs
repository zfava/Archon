using System.Diagnostics;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Models;

/// <summary>
/// Provides structured AI runtime diagnostics that prove provider configuration,
/// readiness, and live execution behavior. Used by the diagnostics endpoint
/// and the startup validation path.
/// </summary>
public sealed class AiRuntimeDiagnostics
{
    private readonly ModelProviderOptions _options;
    private readonly OpenAiModelProvider _openAi;
    private readonly AnthropicModelProvider _anthropic;
    private readonly AzureOpenAiModelProvider _azureOpenAi;
    private readonly LocalModelProvider _local;
    private readonly ILogger<AiRuntimeDiagnostics> _logger;

    public AiRuntimeDiagnostics(
        IOptions<ModelProviderOptions> options,
        OpenAiModelProvider openAi,
        AnthropicModelProvider anthropic,
        AzureOpenAiModelProvider azureOpenAi,
        LocalModelProvider local,
        ILogger<AiRuntimeDiagnostics> logger)
    {
        _options = options.Value;
        _openAi = openAi;
        _anthropic = anthropic;
        _azureOpenAi = azureOpenAi;
        _local = local;
        _logger = logger;
    }

    /// <summary>
    /// Returns a structured environment report showing which providers are configured,
    /// which have valid keys, and the overall readiness tier.
    /// </summary>
    public EnvironmentReport GetEnvironmentReport()
    {
        var providers = new List<ProviderDiagnostic>();

        providers.Add(EvaluateCloudProvider("OpenAI", _options.OpenAI.Enabled,
            _options.OpenAI.ApiKey, _options.OpenAI.Endpoint, _openAi.ProviderName));

        providers.Add(EvaluateCloudProvider("Anthropic", _options.Anthropic.Enabled,
            _options.Anthropic.ApiKey, _options.Anthropic.Endpoint, _anthropic.ProviderName));

        providers.Add(EvaluateAzureProvider());

        providers.Add(new ProviderDiagnostic(
            Name: "Local",
            ProviderType: "local",
            IsEnabled: _options.Local.Enabled,
            HasApiKey: true, // Local does not require API key
            Endpoint: _options.Local.Endpoint,
            Status: _options.Local.Enabled ? "ready" : "disabled",
            DefaultModel: _options.Local.DefaultModel,
            FailureReason: null));

        int cloudActive = providers.Count(p => p.ProviderType == "cloud" && p.Status == "ready");
        bool localActive = providers.Any(p => p.ProviderType == "local" && p.Status == "ready");

        string readinessTier;
        string readinessSummary;

        if (cloudActive >= 2)
        {
            readinessTier = "production";
            readinessSummary = $"{cloudActive} cloud providers active with redundancy.";
        }
        else if (cloudActive == 1)
        {
            readinessTier = "production-single";
            readinessSummary = "1 cloud provider active. Add a second for redundancy.";
        }
        else if (localActive)
        {
            readinessTier = "local-only";
            readinessSummary = "No cloud providers configured. Only local model server available. Not suitable for production.";
        }
        else
        {
            readinessTier = "unconfigured";
            readinessSummary = "ZERO providers active. All AI requests will fail. Configure OPENAI_API_KEY or ANTHROPIC_API_KEY.";
        }

        return new EnvironmentReport(
            CheckedAtUtc: DateTimeOffset.UtcNow,
            DefaultModel: _options.DefaultModel,
            ReadinessTier: readinessTier,
            ReadinessSummary: readinessSummary,
            CloudProvidersActive: cloudActive,
            LocalProviderActive: localActive,
            Providers: providers);
    }

    /// <summary>
    /// Runs a live smoke test against each configured provider by sending a minimal prompt
    /// and validating the response structure. Returns per-provider results with latency,
    /// token usage, and success/failure details.
    /// </summary>
    public async Task<SmokeTestReport> RunSmokeTestAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("AI runtime smoke test starting...");
        var results = new List<SmokeTestResult>();

        var providerInstances = new (string Name, IModelProvider Provider, string TestModel, bool RequiresApiKey, bool HasApiKey)[]
        {
            ("OpenAI", _openAi, "openai.gpt-4.1-mini", true, !string.IsNullOrWhiteSpace(_options.OpenAI.ApiKey)),
            ("Anthropic", _anthropic, "anthropic.claude-haiku-4-5-20251001", true, !string.IsNullOrWhiteSpace(_options.Anthropic.ApiKey)),
            ("AzureOpenAI", _azureOpenAi, $"azure.{_options.AzureOpenAI.Deployment}", true, !string.IsNullOrWhiteSpace(_options.AzureOpenAI.ApiKey)),
            ("Local", _local, _options.Local.DefaultModel, false, true),
        };

        foreach (var (name, provider, testModel, requiresApiKey, hasApiKey) in providerInstances)
        {
            if (requiresApiKey && !hasApiKey)
            {
                results.Add(new SmokeTestResult(
                    ProviderName: name,
                    Model: testModel,
                    Status: "skipped",
                    LatencyMs: null,
                    TokenUsage: null,
                    FinishReason: null,
                    Error: "API key not configured — skipped.",
                    TestedAtUtc: DateTimeOffset.UtcNow));
                continue;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                var request = new ModelRequest(
                    Model: testModel,
                    Prompt: "Respond with exactly: ARCHONAI_RUNTIME_VERIFIED",
                    Parameters: new Dictionary<string, string>(),
                    RequestedBy: "AiRuntimeDiagnostics",
                    RequestedAtUtc: DateTimeOffset.UtcNow)
                {
                    MaxTokens = 20,
                    Temperature = 0.0,
                    Timeout = TimeSpan.FromSeconds(30),
                };

                var response = await provider.GenerateAsync(request, cancellationToken);
                sw.Stop();

                results.Add(new SmokeTestResult(
                    ProviderName: name,
                    Model: response.Model,
                    Status: response.IsSuccess ? "pass" : "fail",
                    LatencyMs: sw.Elapsed.TotalMilliseconds,
                    TokenUsage: response.Usage is not null
                        ? new SmokeTestTokenUsage(response.Usage.PromptTokens, response.Usage.CompletionTokens, response.Usage.TotalTokens)
                        : null,
                    FinishReason: response.FinishReason,
                    Error: response.IsSuccess ? null : string.Join("; ", response.Errors),
                    TestedAtUtc: DateTimeOffset.UtcNow));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                results.Add(new SmokeTestResult(
                    ProviderName: name,
                    Model: testModel,
                    Status: "error",
                    LatencyMs: sw.Elapsed.TotalMilliseconds,
                    TokenUsage: null,
                    FinishReason: null,
                    Error: ex.Message,
                    TestedAtUtc: DateTimeOffset.UtcNow));
            }
        }

        int passed = results.Count(r => r.Status == "pass");
        int failed = results.Count(r => r.Status == "fail" || r.Status == "error");
        int skipped = results.Count(r => r.Status == "skipped");

        var report = new SmokeTestReport(
            TestedAtUtc: DateTimeOffset.UtcNow,
            TotalProviders: results.Count,
            Passed: passed,
            Failed: failed,
            Skipped: skipped,
            OverallStatus: passed > 0 ? "pass" : (failed > 0 ? "fail" : "no-providers"),
            Results: results);

        _logger.LogInformation(
            "AI runtime smoke test complete: {Passed}/{Total} passed, {Failed} failed, {Skipped} skipped",
            passed, results.Count, failed, skipped);

        return report;
    }

    private static ProviderDiagnostic EvaluateCloudProvider(
        string name, bool enabled, string apiKey, string endpoint, string providerType)
    {
        if (!enabled)
            return new ProviderDiagnostic(name, "cloud", false, false, endpoint, "disabled", null, "Disabled in configuration");

        bool hasKey = !string.IsNullOrWhiteSpace(apiKey);
        if (!hasKey)
            return new ProviderDiagnostic(name, "cloud", true, false, endpoint, "misconfigured", null, "API key not configured");

        return new ProviderDiagnostic(name, "cloud", true, true, endpoint, "ready", null, null);
    }

    private ProviderDiagnostic EvaluateAzureProvider()
    {
        if (!_options.AzureOpenAI.Enabled)
            return new ProviderDiagnostic("AzureOpenAI", "cloud", false, false, null, "disabled", _options.AzureOpenAI.Deployment, "Disabled in configuration");

        bool hasKey = !string.IsNullOrWhiteSpace(_options.AzureOpenAI.ApiKey);
        bool hasEndpoint = !string.IsNullOrWhiteSpace(_options.AzureOpenAI.Endpoint);

        if (!hasKey)
            return new ProviderDiagnostic("AzureOpenAI", "cloud", true, false, _options.AzureOpenAI.Endpoint, "misconfigured", _options.AzureOpenAI.Deployment, "API key not configured");
        if (!hasEndpoint)
            return new ProviderDiagnostic("AzureOpenAI", "cloud", true, true, null, "misconfigured", _options.AzureOpenAI.Deployment, "Endpoint not configured");

        return new ProviderDiagnostic("AzureOpenAI", "cloud", true, true, _options.AzureOpenAI.Endpoint, "ready", _options.AzureOpenAI.Deployment, null);
    }
}

// ── Diagnostic DTOs ──────────────────────────────────────────────────

public sealed record EnvironmentReport(
    DateTimeOffset CheckedAtUtc,
    string DefaultModel,
    string ReadinessTier,
    string ReadinessSummary,
    int CloudProvidersActive,
    bool LocalProviderActive,
    IReadOnlyList<ProviderDiagnostic> Providers);

public sealed record ProviderDiagnostic(
    string Name,
    string ProviderType,
    bool IsEnabled,
    bool HasApiKey,
    string? Endpoint,
    string Status,
    string? DefaultModel,
    string? FailureReason);

public sealed record SmokeTestReport(
    DateTimeOffset TestedAtUtc,
    int TotalProviders,
    int Passed,
    int Failed,
    int Skipped,
    string OverallStatus,
    IReadOnlyList<SmokeTestResult> Results);

public sealed record SmokeTestResult(
    string ProviderName,
    string Model,
    string Status,
    double? LatencyMs,
    SmokeTestTokenUsage? TokenUsage,
    string? FinishReason,
    string? Error,
    DateTimeOffset TestedAtUtc);

public sealed record SmokeTestTokenUsage(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens);
