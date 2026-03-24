using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models;
using ArchonAI.Core.Models.Models.Routing;
using ArchonAI.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArchonAI.Tests;

/// <summary>
/// Tests for the AI model provider infrastructure:
/// - Provider abstraction correctness
/// - Output validation and JSON repair
/// - Timeout/cancellation propagation
/// - Error handling and failure visibility
/// - Model capability registry
/// </summary>
public class ModelProviderTests
{
    private static ModelProviderOptions DefaultOptions(
        string? openAiKey = null,
        string? anthropicKey = null) => new()
    {
        OpenAI = new OpenAiOptions { ApiKey = openAiKey ?? "", Endpoint = "https://api.openai.com" },
        Anthropic = new AnthropicOptions { ApiKey = anthropicKey ?? "", Endpoint = "https://api.anthropic.com" },
        AzureOpenAI = new AzureOpenAiOptions { ApiKey = "", Endpoint = "" },
        Local = new LocalModelOptions { Endpoint = "http://localhost:11434", DefaultModel = "local.default" },
    };

    private static ModelRequest MakeRequest(string model = "openai.gpt-4.1", string prompt = "Hello") =>
        new(Model: model, Prompt: prompt,
            Parameters: new Dictionary<string, string>(),
            RequestedBy: "test",
            RequestedAtUtc: DateTimeOffset.UtcNow);

    // ── Provider Abstraction Tests ───────────────────────────────────

    [Fact]
    public void OpenAi_CanHandle_CorrectPrefix()
    {
        var provider = CreateOpenAiProvider();
        Assert.True(provider.CanHandle("openai.gpt-4.1"));
        Assert.True(provider.CanHandle("OpenAI.gpt-4.1-mini"));
        Assert.False(provider.CanHandle("anthropic.claude-sonnet-4-6"));
        Assert.False(provider.CanHandle("local.default"));
    }

    [Fact]
    public void Anthropic_CanHandle_CorrectPrefix()
    {
        var provider = CreateAnthropicProvider();
        Assert.True(provider.CanHandle("anthropic.claude-sonnet-4-6"));
        Assert.False(provider.CanHandle("openai.gpt-4.1"));
    }

    [Fact]
    public void AzureOpenAi_CanHandle_CorrectPrefix()
    {
        var provider = CreateAzureOpenAiProvider();
        Assert.True(provider.CanHandle("azure.gpt-4o-mini"));
        Assert.False(provider.CanHandle("openai.gpt-4.1"));
    }

    [Fact]
    public void Local_CanHandle_MultipleFormats()
    {
        var provider = CreateLocalProvider();
        Assert.True(provider.CanHandle("local.default"));
        Assert.True(provider.CanHandle("ollama.llama3"));
        Assert.True(provider.CanHandle("gguf.mistral"));
        Assert.False(provider.CanHandle("openai.gpt-4.1"));
    }

    // ── Missing API Key Tests ────────────────────────────────────────

    [Fact]
    public async Task OpenAi_MissingApiKey_ReturnsExplicitError()
    {
        var provider = CreateOpenAiProvider(apiKey: "");
        var response = await provider.GenerateAsync(MakeRequest());

        Assert.False(response.IsSuccess);
        Assert.Contains("API key is not configured", response.Errors[0]);
        Assert.NotNull(response.CorrelationId);
    }

    [Fact]
    public async Task Anthropic_MissingApiKey_ReturnsExplicitError()
    {
        var provider = CreateAnthropicProvider(apiKey: "");
        var response = await provider.GenerateAsync(MakeRequest("anthropic.claude-sonnet-4-6"));

        Assert.False(response.IsSuccess);
        Assert.Contains("API key is not configured", response.Errors[0]);
    }

    [Fact]
    public async Task AzureOpenAi_MissingConfig_ReturnsExplicitError()
    {
        var provider = CreateAzureOpenAiProvider();
        var response = await provider.GenerateAsync(MakeRequest("azure.gpt-4o-mini"));

        Assert.False(response.IsSuccess);
        Assert.Contains("not configured", response.Errors[0]);
    }

    // ── Cancellation Propagation Tests ───────────────────────────────

    [Fact]
    public async Task OpenAi_CallerCancellation_PropagatesImmediately()
    {
        var provider = CreateOpenAiProvider(apiKey: "sk-test-key");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.GenerateAsync(MakeRequest(), cts.Token));
    }

    [Fact]
    public async Task Anthropic_CallerCancellation_PropagatesImmediately()
    {
        var provider = CreateAnthropicProvider(apiKey: "sk-test-key");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.GenerateAsync(MakeRequest("anthropic.claude-sonnet-4-6"), cts.Token));
    }

    // ── Local Provider Failure Tests ────────────────────────────────

    [Fact]
    public async Task Local_UnreachableServer_ReturnsExplicitFailure()
    {
        var provider = CreateLocalProvider();
        var request = MakeRequest("local.default", "Test prompt");
        var response = await provider.GenerateAsync(request);

        Assert.False(response.IsSuccess);
        Assert.Equal(string.Empty, response.Content);
        Assert.Equal("provider_unavailable", response.FinishReason);
        Assert.Contains(response.Errors, e => e.Contains("Local model server unavailable"));
    }

    [Fact]
    public async Task Local_UnreachableServer_NeverReturnsEchoContent()
    {
        var provider = CreateLocalProvider();
        var response = await provider.GenerateAsync(MakeRequest("local.default", "What is 2+2?"));

        Assert.False(response.IsSuccess);
        Assert.DoesNotContain("What is 2+2?", response.Content);
        Assert.DoesNotContain("echo", response.Content);
        Assert.NotEqual("echo_fallback", response.FinishReason);
    }

    [Fact]
    public async Task Local_UnreachableServer_SuggestsCloudProvider()
    {
        var provider = CreateLocalProvider();
        var response = await provider.GenerateAsync(MakeRequest("local.default", "Test"));

        Assert.Contains(response.Errors, e => e.Contains("OpenAI/Anthropic") || e.Contains("cloud provider"));
    }

    // ── Correlation ID Tests ─────────────────────────────────────────

    [Fact]
    public async Task Response_PreservesCorrelationId()
    {
        var provider = CreateOpenAiProvider(apiKey: "");
        var request = MakeRequest() with { };
        var response = await provider.GenerateAsync(request);

        Assert.Equal(request.CorrelationId, response.CorrelationId);
    }

    [Fact]
    public void ModelRequest_GeneratesUniqueCorrelationIds()
    {
        var r1 = MakeRequest();
        var r2 = MakeRequest();
        Assert.NotEqual(r1.CorrelationId, r2.CorrelationId);
    }

    // ── Output Validation Tests ──────────────────────────────────────

    [Fact]
    public void Validator_ValidJson_PassesValidation()
    {
        var validator = new ModelOutputValidator();
        var response = MakeSuccessResponse("""{"name": "test", "value": 42}""");
        var schema = """{"type":"object","required":["name","value"],"properties":{"name":{"type":"string"},"value":{"type":"integer"}}}""";

        var result = validator.ValidateAndRepair(response, schema);

        Assert.True(result.SchemaValid);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("SCHEMA_MISSING_FIELD"));
    }

    [Fact]
    public void Validator_MissingRequiredField_ReportsSchemaInvalid()
    {
        var validator = new ModelOutputValidator();
        var response = MakeSuccessResponse("""{"name": "test"}""");
        var schema = """{"type":"object","required":["name","value"],"properties":{"name":{"type":"string"},"value":{"type":"integer"}}}""";

        var result = validator.ValidateAndRepair(response, schema);

        Assert.False(result.SchemaValid);
        Assert.Contains(result.Warnings, w => w.Contains("SCHEMA_MISSING_FIELD") && w.Contains("value"));
    }

    [Fact]
    public void Validator_MarkdownCodeBlock_ExtractsJson()
    {
        var validator = new ModelOutputValidator();
        var content = "Here is the result:\n```json\n{\"key\": \"value\"}\n```\nDone!";
        var response = MakeSuccessResponse(content);
        var schema = """{"type":"object","required":["key"],"properties":{"key":{"type":"string"}}}""";

        var result = validator.ValidateAndRepair(response, schema);

        Assert.True(result.SchemaValid);
        Assert.Contains(result.Warnings, w => w.Contains("JSON_REPAIRED"));
    }

    [Fact]
    public void Validator_TrailingComma_RepairsJson()
    {
        var validator = new ModelOutputValidator();
        var content = """{"key": "value", "items": [1, 2, 3,]}""";
        var response = MakeSuccessResponse(content);
        var schema = """{"type":"object","required":["key"],"properties":{"key":{"type":"string"}}}""";

        var result = validator.ValidateAndRepair(response, schema);

        Assert.True(result.SchemaValid);
        Assert.Contains(result.Warnings, w => w.Contains("JSON_REPAIRED"));
    }

    [Fact]
    public void Validator_CompletelyInvalidJson_MarksSchemaInvalid()
    {
        var validator = new ModelOutputValidator();
        var response = MakeSuccessResponse("This is not JSON at all.");
        var schema = """{"type":"object","required":["key"],"properties":{"key":{"type":"string"}}}""";

        var result = validator.ValidateAndRepair(response, schema);

        Assert.False(result.SchemaValid);
        Assert.Contains(result.Warnings, w => w.Contains("JSON_INVALID"));
    }

    [Fact]
    public void Validator_NullSchema_SkipsValidation()
    {
        var validator = new ModelOutputValidator();
        var response = MakeSuccessResponse("Not JSON");

        var result = validator.ValidateAndRepair(response, null);

        Assert.Null(result.SchemaValid);
    }

    [Fact]
    public void Validator_FailedResponse_SkipsValidation()
    {
        var validator = new ModelOutputValidator();
        var response = new ModelResponse("openai", "gpt-4.1", false, "",
            Array.Empty<string>(), new[] { "Provider error" }, DateTimeOffset.UtcNow);
        var schema = """{"type":"object","required":["key"]}""";

        var result = validator.ValidateAndRepair(response, schema);

        Assert.False(result.IsSuccess);
        Assert.Null(result.SchemaValid);
    }

    // ── Model Capability Registry Tests ──────────────────────────────

    [Fact]
    public void Registry_ContainsOpenAiModels()
    {
        Assert.NotNull(ModelCapabilityRegistry.Get("openai.gpt-4.1"));
        Assert.NotNull(ModelCapabilityRegistry.Get("openai.gpt-4.1-mini"));
    }

    [Fact]
    public void Registry_ContainsAnthropicModels()
    {
        Assert.NotNull(ModelCapabilityRegistry.Get("anthropic.claude-sonnet-4-6"));
        Assert.NotNull(ModelCapabilityRegistry.Get("anthropic.claude-opus-4-6"));
    }

    [Fact]
    public void Registry_CaseInsensitiveLookup()
    {
        Assert.NotNull(ModelCapabilityRegistry.Get("OpenAI.GPT-4.1"));
    }

    [Fact]
    public void Registry_UnknownModel_ReturnsNull()
    {
        Assert.Null(ModelCapabilityRegistry.Get("nonexistent.model"));
    }

    [Fact]
    public void Registry_Models_HavePositiveCosts()
    {
        foreach (var (key, cap) in ModelCapabilityRegistry.All)
        {
            if (cap.Provider == "local") continue; // Local is free
            Assert.True(cap.CostPerInputToken > 0, $"{key} should have positive input cost");
            Assert.True(cap.CostPerOutputToken > 0, $"{key} should have positive output cost");
        }
    }

    [Fact]
    public void Registry_Models_HaveReasonableContextLimits()
    {
        foreach (var (key, cap) in ModelCapabilityRegistry.All)
        {
            Assert.True(cap.MaxContextTokens > 0, $"{key} should have positive context limit");
            Assert.True(cap.MaxOutputTokens > 0, $"{key} should have positive output limit");
        }
    }

    // ── TokenUsage Tests ─────────────────────────────────────────────

    [Fact]
    public void TokenUsage_TotalIsSum()
    {
        var usage = new TokenUsage(100, 50, 150);
        Assert.Equal(150, usage.TotalTokens);
        Assert.Equal(100, usage.PromptTokens);
        Assert.Equal(50, usage.CompletionTokens);
    }

    // ── JSON Repair Internal Tests ───────────────────────────────────

    [Fact]
    public void JsonRepair_ExtractsFromCodeBlock()
    {
        var input = "Sure!\n```json\n{\"a\":1}\n```";
        var result = ModelOutputValidator.AttemptJsonRepair(input);
        Assert.Equal("{\"a\":1}", result);
    }

    [Fact]
    public void JsonRepair_RemovesTrailingCommas()
    {
        var input = "{\"a\":1, \"b\":2,}";
        var result = ModelOutputValidator.AttemptJsonRepair(input);
        Assert.Equal("{\"a\":1, \"b\":2}", result);
    }

    // ── Default Routing Configuration Tests ─────────────────────────

    [Fact]
    public void DefaultModel_IsCloudProvider_NotLocal()
    {
        var options = new ModelProviderOptions();
        Assert.DoesNotContain("local", options.DefaultModel, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("openai", options.DefaultModel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModelRouterDefaults_PreferCloudProviders()
    {
        var options = new ArchonAI.ModelRouter.ModelRouterOptions();
        Assert.NotEqual("local", options.DefaultProvider);
        Assert.DoesNotContain("local", options.DefaultModel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("local", options.CostOptimizedModel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FallbackChains_LocalIsLastResort()
    {
        var options = new ArchonAI.ModelRouter.ModelRouterOptions();
        foreach (var (chainName, entries) in options.FallbackChains)
        {
            var localEntry = entries.FirstOrDefault(e => e.Provider == "local");
            if (localEntry is not null)
            {
                var maxPriority = entries.Max(e => e.Priority);
                Assert.Equal(maxPriority, localEntry.Priority);
            }
        }
    }

    [Fact]
    public void TaskTypeModelMap_NoLocalForNonLightweight()
    {
        var options = new ArchonAI.ModelRouter.ModelRouterOptions();
        foreach (var (taskType, model) in options.TaskTypeModelMap)
        {
            // No task type should route to local by default
            Assert.DoesNotContain("local", model, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── Helper methods ───────────────────────────────────────────────

    private static OpenAiModelProvider CreateOpenAiProvider(string? apiKey = null) =>
        new(Options.Create(DefaultOptions(openAiKey: apiKey)),
            new MockHttpClientFactory(),
            NullLogger<OpenAiModelProvider>.Instance);

    private static AnthropicModelProvider CreateAnthropicProvider(string? apiKey = null) =>
        new(Options.Create(DefaultOptions(anthropicKey: apiKey)),
            new MockHttpClientFactory(),
            NullLogger<AnthropicModelProvider>.Instance);

    private static AzureOpenAiModelProvider CreateAzureOpenAiProvider() =>
        new(Options.Create(DefaultOptions()),
            new MockHttpClientFactory(),
            NullLogger<AzureOpenAiModelProvider>.Instance);

    private static LocalModelProvider CreateLocalProvider() =>
        new(Options.Create(DefaultOptions()),
            new MockHttpClientFactory(),
            NullLogger<LocalModelProvider>.Instance);

    private static ModelResponse MakeSuccessResponse(string content) =>
        new("openai", "gpt-4.1", true, content,
            Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow)
        {
            CorrelationId = "test-correlation"
        };

    /// <summary>Simple factory that returns a default HttpClient (will fail on real HTTP calls).</summary>
    private sealed class MockHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
