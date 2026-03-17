namespace ArchonAI.Core.Models.Models;

public sealed record ModelRequest(
    string Model,
    string Prompt,
    IReadOnlyDictionary<string, string> Parameters,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc)
{
    /// <summary>Correlation ID for distributed tracing.</summary>
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Optional system prompt prepended to the conversation.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Maximum tokens to generate.</summary>
    public int? MaxTokens { get; init; }

    /// <summary>Sampling temperature (0.0–2.0).</summary>
    public double? Temperature { get; init; }

    /// <summary>Optional JSON schema the response must conform to.</summary>
    public string? ResponseJsonSchema { get; init; }

    /// <summary>Per-request timeout override (otherwise provider default applies).</summary>
    public TimeSpan? Timeout { get; init; }
}
