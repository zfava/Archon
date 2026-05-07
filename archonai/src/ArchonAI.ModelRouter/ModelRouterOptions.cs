namespace ArchonAI.ModelRouter;

public sealed class ModelRouterOptions
{
    public string DefaultProvider { get; set; } = "openai";

    public string DefaultModel { get; set; } = "openai.gpt-4.1-mini";

    public string CostOptimizedProvider { get; set; } = "openai";

    public string CostOptimizedModel { get; set; } = "openai.gpt-4.1-nano";

    public string LatencyOptimizedProvider { get; set; } = "azure-openai";

    public string LatencyOptimizedModel { get; set; } = "azure.gpt-4o-mini";

    public string QualityOptimizedProvider { get; set; } = "openai";

    public string QualityOptimizedModel { get; set; } = "openai.gpt-4.1";

    public IReadOnlyDictionary<string, string> TaskTypeModelMap { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["analysis"] = "openai.gpt-4.1",
        ["classification"] = "azure.gpt-4o-mini",
        ["extraction"] = "anthropic.claude-sonnet-4-6",
        ["drafting"] = "openai.gpt-4.1",
        ["lightweight"] = "openai.gpt-4.1-nano"
    };

    public bool EnableAdaptiveRouting { get; set; } = true;

    public int MinSamplesForAdaptive { get; set; } = 10;

    public IReadOnlyDictionary<string, List<FallbackEntryOptions>> FallbackChains { get; set; } = new Dictionary<string, List<FallbackEntryOptions>>(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = new()
        {
            new FallbackEntryOptions { Provider = "openai", Model = "openai.gpt-4.1", Priority = 1 },
            new FallbackEntryOptions { Provider = "azure-openai", Model = "azure.gpt-4o-mini", Priority = 2 },
            new FallbackEntryOptions { Provider = "anthropic", Model = "anthropic.claude-sonnet-4-6", Priority = 3 },
            new FallbackEntryOptions { Provider = "local", Model = "local.default", Priority = 4 }
        },
        ["cost"] = new()
        {
            new FallbackEntryOptions { Provider = "openai", Model = "openai.gpt-4.1-nano", Priority = 1 },
            new FallbackEntryOptions { Provider = "azure-openai", Model = "azure.gpt-4o-mini", Priority = 2 },
            new FallbackEntryOptions { Provider = "anthropic", Model = "anthropic.claude-haiku-4-5", Priority = 3 },
            new FallbackEntryOptions { Provider = "local", Model = "local.default", Priority = 4 }
        },
        ["latency"] = new()
        {
            new FallbackEntryOptions { Provider = "azure-openai", Model = "azure.gpt-4o-mini", Priority = 1 },
            new FallbackEntryOptions { Provider = "openai", Model = "openai.gpt-4.1-mini", Priority = 2 },
            new FallbackEntryOptions { Provider = "anthropic", Model = "anthropic.claude-haiku-4-5", Priority = 3 },
            new FallbackEntryOptions { Provider = "local", Model = "local.default", Priority = 4 }
        },
        ["quality"] = new()
        {
            new FallbackEntryOptions { Provider = "openai", Model = "openai.gpt-4.1", Priority = 1 },
            new FallbackEntryOptions { Provider = "anthropic", Model = "anthropic.claude-sonnet-4-6", Priority = 2 },
            new FallbackEntryOptions { Provider = "azure-openai", Model = "azure.gpt-4o-mini", Priority = 3 }
        }
    };
}

public sealed class FallbackEntryOptions
{
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Priority { get; set; }
}
