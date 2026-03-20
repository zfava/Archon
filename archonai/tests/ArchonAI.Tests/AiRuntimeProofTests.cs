using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models;
using ArchonAI.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArchonAI.Tests;

/// <summary>
/// AI runtime proof tests that verify:
/// - Environment validation correctly classifies provider readiness tiers
/// - Missing API key behavior is explicit and honest (no echo/stub)
/// - All-providers-unavailable path returns structured failure
/// - Local-only path is clearly distinguished from cloud execution
/// - Diagnostics report accurately reflects configuration state
///
/// These tests run against the real production code paths and prove
/// that provider configuration is enforced at runtime.
/// </summary>
[Trait("Category", "RuntimeProof")]
[Trait("Subsystem", "AiRuntime")]
public sealed class AiRuntimeProofTests
{
    // ── Environment Report Tests ────────────────────────────────────

    [Fact]
    public void EnvironmentReport_NoKeys_ReturnsUnconfiguredTier()
    {
        var diag = CreateDiagnostics(openAiKey: "", anthropicKey: "", azureKey: "", localEnabled: false);
        var report = diag.GetEnvironmentReport();

        Assert.Equal("unconfigured", report.ReadinessTier);
        Assert.Equal(0, report.CloudProvidersActive);
        Assert.False(report.LocalProviderActive);
        Assert.Contains("ZERO", report.ReadinessSummary);
    }

    [Fact]
    public void EnvironmentReport_LocalOnly_ReturnsLocalOnlyTier()
    {
        var diag = CreateDiagnostics(openAiKey: "", anthropicKey: "", azureKey: "", localEnabled: true);
        var report = diag.GetEnvironmentReport();

        Assert.Equal("local-only", report.ReadinessTier);
        Assert.Equal(0, report.CloudProvidersActive);
        Assert.True(report.LocalProviderActive);
        Assert.Contains("Not suitable for production", report.ReadinessSummary);
    }

    [Fact]
    public void EnvironmentReport_SingleCloudProvider_ReturnsSingleTier()
    {
        var diag = CreateDiagnostics(openAiKey: "sk-test", anthropicKey: "", azureKey: "");
        var report = diag.GetEnvironmentReport();

        Assert.Equal("production-single", report.ReadinessTier);
        Assert.Equal(1, report.CloudProvidersActive);
        Assert.Contains("redundancy", report.ReadinessSummary);
    }

    [Fact]
    public void EnvironmentReport_TwoCloudProviders_ReturnsProductionTier()
    {
        var diag = CreateDiagnostics(openAiKey: "sk-test", anthropicKey: "sk-ant-test", azureKey: "");
        var report = diag.GetEnvironmentReport();

        Assert.Equal("production", report.ReadinessTier);
        Assert.Equal(2, report.CloudProvidersActive);
    }

    [Fact]
    public void EnvironmentReport_AllProviders_ReturnsProductionTier()
    {
        var diag = CreateDiagnostics(
            openAiKey: "sk-test",
            anthropicKey: "sk-ant-test",
            azureKey: "azure-key",
            azureEndpoint: "https://myinstance.openai.azure.com");
        var report = diag.GetEnvironmentReport();

        Assert.Equal("production", report.ReadinessTier);
        Assert.Equal(3, report.CloudProvidersActive);
    }

    [Fact]
    public void EnvironmentReport_ContainsAllProviderEntries()
    {
        var diag = CreateDiagnostics(openAiKey: "", anthropicKey: "", azureKey: "");
        var report = diag.GetEnvironmentReport();

        Assert.Equal(4, report.Providers.Count);
        Assert.Contains(report.Providers, p => p.Name == "OpenAI");
        Assert.Contains(report.Providers, p => p.Name == "Anthropic");
        Assert.Contains(report.Providers, p => p.Name == "AzureOpenAI");
        Assert.Contains(report.Providers, p => p.Name == "Local");
    }

    [Fact]
    public void EnvironmentReport_MisconfiguredProvider_ShowsReason()
    {
        var diag = CreateDiagnostics(openAiKey: "", anthropicKey: "sk-ant-test", azureKey: "");
        var report = diag.GetEnvironmentReport();

        var openai = report.Providers.First(p => p.Name == "OpenAI");
        Assert.Equal("misconfigured", openai.Status);
        Assert.Equal("API key not configured", openai.FailureReason);

        var anthropic = report.Providers.First(p => p.Name == "Anthropic");
        Assert.Equal("ready", anthropic.Status);
        Assert.Null(anthropic.FailureReason);
    }

    [Fact]
    public void EnvironmentReport_DisabledProvider_ShowsDisabled()
    {
        var options = CreateOptions(openAiKey: "sk-test", anthropicKey: "");
        options.OpenAI.Enabled = false;

        var diag = CreateDiagnosticsFromOptions(options);
        var report = diag.GetEnvironmentReport();

        var openai = report.Providers.First(p => p.Name == "OpenAI");
        Assert.Equal("disabled", openai.Status);
        Assert.False(openai.IsEnabled);
    }

    [Fact]
    public void EnvironmentReport_AzureMissingEndpoint_ShowsMisconfigured()
    {
        var diag = CreateDiagnostics(openAiKey: "", anthropicKey: "", azureKey: "azure-key", azureEndpoint: "");
        var report = diag.GetEnvironmentReport();

        var azure = report.Providers.First(p => p.Name == "AzureOpenAI");
        Assert.Equal("misconfigured", azure.Status);
        Assert.Contains("Endpoint", azure.FailureReason);
    }

    // ── Smoke Test Skipping Tests ───────────────────────────────────

    [Fact]
    public async Task SmokeTest_NoKeys_AllSkippedOrFailed()
    {
        var diag = CreateDiagnostics(openAiKey: "", anthropicKey: "", azureKey: "", localEnabled: false);
        var report = await diag.RunSmokeTestAsync();

        Assert.Equal(0, report.Passed);
        // Overall status is "fail" or "no-providers" depending on whether disabled local counts
        Assert.True(report.OverallStatus == "fail" || report.OverallStatus == "no-providers",
            $"Expected fail or no-providers, got {report.OverallStatus}");

        // Cloud providers without keys should be skipped
        foreach (var result in report.Results.Where(r => r.ProviderName != "Local"))
        {
            Assert.Equal("skipped", result.Status);
            Assert.Contains("API key not configured", result.Error);
        }
    }

    [Fact]
    public async Task SmokeTest_LocalUnavailable_ReturnsExplicitFailure()
    {
        var diag = CreateDiagnostics(openAiKey: "", anthropicKey: "", azureKey: "", localEnabled: true);
        var report = await diag.RunSmokeTestAsync();

        var localResult = report.Results.First(r => r.ProviderName == "Local");
        // Local should fail (no Ollama running) but with an honest error
        Assert.True(localResult.Status == "fail" || localResult.Status == "error");
        Assert.NotNull(localResult.Error);
        Assert.NotNull(localResult.LatencyMs);
    }

    // ── Missing-Key Provider Behavior Tests ─────────────────────────

    [Fact]
    public async Task OpenAi_MissingKey_ReturnsStructuredError_NoEcho()
    {
        var provider = CreateOpenAiProvider(apiKey: "");
        var response = await provider.GenerateAsync(MakeRequest("openai.gpt-4.1"));

        Assert.False(response.IsSuccess);
        Assert.Equal(string.Empty, response.Content);
        Assert.Single(response.Errors);
        Assert.Contains("API key", response.Errors[0]);
        Assert.NotEqual("echo_fallback", response.FinishReason);
    }

    [Fact]
    public async Task Anthropic_MissingKey_ReturnsStructuredError_NoEcho()
    {
        var provider = CreateAnthropicProvider(apiKey: "");
        var response = await provider.GenerateAsync(MakeRequest("anthropic.claude-sonnet-4-6"));

        Assert.False(response.IsSuccess);
        Assert.Equal(string.Empty, response.Content);
        Assert.Contains("API key", response.Errors[0]);
    }

    [Fact]
    public async Task Local_Unavailable_ReturnsStructuredError_NoEcho()
    {
        var provider = CreateLocalProvider();
        var response = await provider.GenerateAsync(MakeRequest("local.default"));

        Assert.False(response.IsSuccess);
        Assert.Equal(string.Empty, response.Content);
        Assert.Equal("provider_unavailable", response.FinishReason);
        Assert.DoesNotContain("echo", response.Content);
    }

    // ── No-Echo Guarantee Tests ─────────────────────────────────────

    [Fact]
    public async Task NoProvider_NeverEchoesUserInput()
    {
        string userPrompt = "What is the capital of France?";

        var openAi = CreateOpenAiProvider(apiKey: "");
        var anthropic = CreateAnthropicProvider(apiKey: "");
        var local = CreateLocalProvider();

        var providers = new IModelProvider[] { openAi, anthropic, local };
        foreach (var provider in providers)
        {
            string model = provider.ProviderName switch
            {
                "openai" => "openai.gpt-4.1",
                "anthropic" => "anthropic.claude-sonnet-4-6",
                _ => "local.default"
            };

            var response = await provider.GenerateAsync(MakeRequest(model, userPrompt));

            Assert.False(response.IsSuccess,
                $"{provider.ProviderName} should not succeed without valid credentials");
            Assert.False(response.Content.Contains(userPrompt),
                $"{provider.ProviderName} must never echo the user prompt in failure mode");
            Assert.True(response.FinishReason != "echo_fallback",
                $"{provider.ProviderName} must never return echo_fallback finish reason");
        }
    }

    // ── Default Configuration Safety Tests ──────────────────────────

    [Fact]
    public void DefaultModel_IsNotLocal()
    {
        var options = new ModelProviderOptions();
        Assert.DoesNotContain("local", options.DefaultModel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnvironmentReport_DefaultModel_NotLocal()
    {
        var diag = CreateDiagnostics(openAiKey: "sk-test", anthropicKey: "");
        var report = diag.GetEnvironmentReport();

        Assert.DoesNotContain("local", report.DefaultModel, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helper methods ──────────────────────────────────────────────

    private static ModelProviderOptions CreateOptions(
        string? openAiKey = null,
        string? anthropicKey = null,
        string? azureKey = null,
        string? azureEndpoint = null,
        bool localEnabled = true) => new()
    {
        OpenAI = new OpenAiOptions { Enabled = true, ApiKey = openAiKey ?? "", Endpoint = "https://api.openai.com" },
        Anthropic = new AnthropicOptions { Enabled = true, ApiKey = anthropicKey ?? "", Endpoint = "https://api.anthropic.com" },
        AzureOpenAI = new AzureOpenAiOptions { Enabled = true, ApiKey = azureKey ?? "", Endpoint = azureEndpoint ?? "" },
        Local = new LocalModelOptions { Enabled = localEnabled, Endpoint = "http://localhost:11434", DefaultModel = "local.default" },
    };

    private static AiRuntimeDiagnostics CreateDiagnostics(
        string? openAiKey = null,
        string? anthropicKey = null,
        string? azureKey = null,
        string? azureEndpoint = null,
        bool localEnabled = true)
    {
        var options = CreateOptions(openAiKey, anthropicKey, azureKey, azureEndpoint, localEnabled);
        return CreateDiagnosticsFromOptions(options);
    }

    private static AiRuntimeDiagnostics CreateDiagnosticsFromOptions(ModelProviderOptions options)
    {
        var opts = Options.Create(options);
        var factory = new MockHttpClientFactory();

        return new AiRuntimeDiagnostics(
            opts,
            new OpenAiModelProvider(opts, factory, NullLogger<OpenAiModelProvider>.Instance),
            new AnthropicModelProvider(opts, factory, NullLogger<AnthropicModelProvider>.Instance),
            new AzureOpenAiModelProvider(opts, factory, NullLogger<AzureOpenAiModelProvider>.Instance),
            new LocalModelProvider(opts, factory, NullLogger<LocalModelProvider>.Instance),
            NullLogger<AiRuntimeDiagnostics>.Instance);
    }

    private static OpenAiModelProvider CreateOpenAiProvider(string apiKey = "") =>
        new(Options.Create(CreateOptions(openAiKey: apiKey)),
            new MockHttpClientFactory(),
            NullLogger<OpenAiModelProvider>.Instance);

    private static AnthropicModelProvider CreateAnthropicProvider(string apiKey = "") =>
        new(Options.Create(CreateOptions(anthropicKey: apiKey)),
            new MockHttpClientFactory(),
            NullLogger<AnthropicModelProvider>.Instance);

    private static LocalModelProvider CreateLocalProvider() =>
        new(Options.Create(CreateOptions()),
            new MockHttpClientFactory(),
            NullLogger<LocalModelProvider>.Instance);

    private static ModelRequest MakeRequest(string model, string prompt = "Hello") =>
        new(Model: model, Prompt: prompt,
            Parameters: new Dictionary<string, string>(),
            RequestedBy: "test",
            RequestedAtUtc: DateTimeOffset.UtcNow);

    private sealed class MockHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
