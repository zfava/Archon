namespace ArchonAI.ModelRouter;

public sealed class ModelRouterOptions
{
    public string DefaultProvider { get; set; } = "local";

    public string DefaultModel { get; set; } = "local.default";

    public string CostOptimizedProvider { get; set; } = "local";

    public string CostOptimizedModel { get; set; } = "local.default";

    public string LatencyOptimizedProvider { get; set; } = "azure-openai";

    public string LatencyOptimizedModel { get; set; } = "azure.gpt-4o-mini";

    public string QualityOptimizedProvider { get; set; } = "openai";

    public string QualityOptimizedModel { get; set; } = "openai.gpt-4.1";

    public IReadOnlyDictionary<string, string> TaskTypeModelMap { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["analysis"] = "openai.gpt-4.1",
        ["classification"] = "azure.gpt-4o-mini",
        ["extraction"] = "anthropic.claude-3-5-sonnet",
        ["drafting"] = "openai.gpt-4.1",
        ["lightweight"] = "local.default"
    };

    public bool EnableAdaptiveRouting { get; set; } = true;

    public int MinSamplesForAdaptive { get; set; } = 10;

    public IReadOnlyDictionary<string, List<FallbackEntryOptions>> FallbackChains { get; set; } = new Dictionary<string, List<FallbackEntryOptions>>(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = new()
        {
            new FallbackEntryOptions { Provider = "openai", Model = "openai.gpt-4.1", Priority = 1 },
            new FallbackEntryOptions { Provider = "azure-openai", Model = "azure.gpt-4o-mini", Priority = 2 },
            new FallbackEntryOptions { Provider = "anthropic", Model = "anthropic.claude-3-5-sonnet", Priority = 3 },
            new FallbackEntryOptions { Provider = "local", Model = "local.default", Priority = 4 }
        },
        ["cost"] = new()
        {
            new FallbackEntryOptions { Provider = "local", Model = "local.default", Priority = 1 },
            new FallbackEntryOptions { Provider = "azure-openai", Model = "azure.gpt-4o-mini", Priority = 2 },
            new FallbackEntryOptions { Provider = "openai", Model = "openai.gpt-4.1", Priority = 3 }
        },
        ["latency"] = new()
        {
            new FallbackEntryOptions { Provider = "azure-openai", Model = "azure.gpt-4o-mini", Priority = 1 },
            new FallbackEntryOptions { Provider = "local", Model = "local.default", Priority = 2 },
            new FallbackEntryOptions { Provider = "openai", Model = "openai.gpt-4.1", Priority = 3 }
        },
        ["quality"] = new()
        {
            new FallbackEntryOptions { Provider = "openai", Model = "openai.gpt-4.1", Priority = 1 },
            new FallbackEntryOptions { Provider = "anthropic", Model = "anthropic.claude-3-5-sonnet", Priority = 2 },
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
