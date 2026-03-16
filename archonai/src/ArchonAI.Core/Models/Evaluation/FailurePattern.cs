namespace ArchonAI.Core.Models.Evaluation;

public sealed record FailurePattern(
    string Pattern,
    int Frequency,
    string Severity);
