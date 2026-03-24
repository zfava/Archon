namespace ArchonAI.Core.Models.Models;

/// <summary>
/// Describes a model's capabilities and constraints for routing decisions.
/// </summary>
public sealed record ModelCapability(
    string Provider,
    string ModelId,
    int MaxContextTokens,
    int MaxOutputTokens,
    bool SupportsJsonMode,
    bool SupportsStreaming,
    bool SupportsVision,
    decimal CostPerInputToken,
    decimal CostPerOutputToken);

/// <summary>Registry of known model capabilities used by the router and accounting.</summary>
public static class ModelCapabilityRegistry
{
    private static readonly Dictionary<string, ModelCapability> _models = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai.gpt-4.1"] = new("openai", "gpt-4.1", 1_047_576, 32_768, true, true, true, 0.000002m, 0.000008m),
        ["openai.gpt-4.1-mini"] = new("openai", "gpt-4.1-mini", 1_047_576, 32_768, true, true, true, 0.0000004m, 0.0000016m),
        ["openai.gpt-4.1-nano"] = new("openai", "gpt-4.1-nano", 1_047_576, 32_768, true, true, false, 0.0000001m, 0.0000004m),
        ["openai.o3-mini"] = new("openai", "o3-mini", 200_000, 100_000, true, true, false, 0.0000011m, 0.0000044m),
        ["anthropic.claude-sonnet-4-6"] = new("anthropic", "claude-sonnet-4-6", 200_000, 16_384, true, true, true, 0.000003m, 0.000015m),
        ["anthropic.claude-haiku-4-5"] = new("anthropic", "claude-haiku-4-5", 200_000, 16_384, true, true, true, 0.0000008m, 0.000004m),
        ["anthropic.claude-opus-4-6"] = new("anthropic", "claude-opus-4-6", 200_000, 32_000, true, true, true, 0.000015m, 0.000075m),
        ["azure.gpt-4o-mini"] = new("azure-openai", "gpt-4o-mini", 128_000, 16_384, true, true, true, 0.00000015m, 0.0000006m),
        ["local.default"] = new("local", "default", 8_192, 2_048, false, false, false, 0m, 0m),
    };

    public static ModelCapability? Get(string modelKey) =>
        _models.TryGetValue(modelKey, out var cap) ? cap : null;

    public static IReadOnlyDictionary<string, ModelCapability> All => _models;
}
