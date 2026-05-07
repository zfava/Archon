namespace ArchonAI.Core.Models.Models;

public sealed record ModelResponse(
    string Provider,
    string Model,
    bool IsSuccess,
    string Content,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    DateTimeOffset CompletedAtUtc)
{
    /// <summary>Correlation ID matching the originating request.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Token usage breakdown.</summary>
    public TokenUsage? Usage { get; init; }

    /// <summary>Wall-clock latency of the provider call.</summary>
    public double? LatencyMs { get; init; }

    /// <summary>Provider-specific finish reason (e.g. "stop", "length", "content_filter").</summary>
    public string? FinishReason { get; init; }

    /// <summary>Whether the response passed JSON schema validation (if schema was requested).</summary>
    public bool? SchemaValid { get; init; }
}

public sealed record TokenUsage(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens);
