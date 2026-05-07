namespace ArchonAI.Patterns;

public sealed class PatternOptions
{
    public int MinRecurringFailureCount { get; set; } = 3;
    public int MinHighPerformanceSuccesses { get; set; } = 3;
    public double HighPerformanceMaxExecutionMs { get; set; } = 1500;
}
