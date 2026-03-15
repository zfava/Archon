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
}
