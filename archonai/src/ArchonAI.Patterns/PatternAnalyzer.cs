using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Patterns;
using ArchonAI.Core.Models.Telemetry;
using Microsoft.Extensions.Options;

namespace ArchonAI.Patterns;

public sealed class PatternAnalyzer : IPatternAnalyzer
{
    private readonly ITaskTelemetryStore _telemetryStore;
    private readonly IMemoryStore _memoryStore;
    private readonly PatternOptions _options;

    public PatternAnalyzer(
        ITaskTelemetryStore telemetryStore,
        IMemoryStore memoryStore,
        IOptions<PatternOptions> options)
    {
        _telemetryStore = telemetryStore;
        _memoryStore = memoryStore;
        _options = options.Value;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<OperationalPattern>> AnalyzeObjectiveAsync(
        Guid objectiveId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TaskExecutionTelemetry> telemetry = await _telemetryStore.QueryByObjectiveAsync(objectiveId, 1000, cancellationToken);
        if (telemetry.Count == 0)
        {
            return Array.Empty<OperationalPattern>();
        }

        var patterns = new List<OperationalPattern>();

        // Detect recurring failures by error type.
        var recurringFailure = telemetry
            .Where(item => !item.Success && !item.ErrorType.Equals("none", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.ErrorType, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { ErrorType = group.Key, Count = group.Count() })
            .Where(group => group.Count >= Math.Max(2, _options.MinRecurringFailureCount))
            .OrderByDescending(group => group.Count)
            .FirstOrDefault();

        if (recurringFailure is not null)
        {
            patterns.Add(new OperationalPattern(
                Id: Guid.NewGuid(),
                ObjectiveId: objectiveId,
                PatternType: "recurring-failure",
                Title: "Recurring failure signature detected",
                Description: $"Error type '{recurringFailure.ErrorType}' repeated {recurringFailure.Count} times.",
                Score: recurringFailure.Count,
                Metadata: new Dictionary<string, string>
                {
                    ["errorType"] = recurringFailure.ErrorType,
                    ["occurrences"] = recurringFailure.Count.ToString()
                },
                DiscoveredAtUtc: DateTimeOffset.UtcNow));
        }

        // Identify high-performance workflows.
        var successItems = telemetry.Where(item => item.Success).ToArray();
        if (successItems.Length >= Math.Max(2, _options.MinHighPerformanceSuccesses))
        {
            double avgExecutionMs = successItems.Average(item => item.ExecutionTimeMs);
            if (avgExecutionMs <= _options.HighPerformanceMaxExecutionMs)
            {
                patterns.Add(new OperationalPattern(
                    Id: Guid.NewGuid(),
                    ObjectiveId: objectiveId,
                    PatternType: "high-performance-workflow",
                    Title: "High performance workflow identified",
                    Description: $"Successful tasks are completing in {avgExecutionMs:F2}ms average.",
                    Score: Math.Max(0.1, 10000.0 / Math.Max(1, avgExecutionMs)),
                    Metadata: new Dictionary<string, string>
                    {
                        ["avgExecutionMs"] = avgExecutionMs.ToString("F2"),
                        ["successCount"] = successItems.Length.ToString()
                    },
                    DiscoveredAtUtc: DateTimeOffset.UtcNow));
            }
        }

        // Rank agent effectiveness.
        var rankedAgents = telemetry
            .GroupBy(item => item.AgentId)
            .Select(group =>
            {
                int total = group.Count();
                int successCount = group.Count(item => item.Success);
                double successRate = total == 0 ? 0 : (double)successCount / total;
                double avgExecutionMs = group.Average(item => item.ExecutionTimeMs);
                double score = (successRate * 100) - (avgExecutionMs / 1000.0);
                return new
                {
                    AgentId = group.Key,
                    Total = total,
                    SuccessRate = successRate,
                    AvgExecutionMs = avgExecutionMs,
                    Score = score
                };
            })
            .OrderByDescending(item => item.Score)
            .ToArray();

        foreach (var rank in rankedAgents)
        {
            patterns.Add(new OperationalPattern(
                Id: Guid.NewGuid(),
                ObjectiveId: objectiveId,
                PatternType: "agent-effectiveness",
                Title: "Agent effectiveness rank",
                Description: $"Agent {rank.AgentId} success rate {(rank.SuccessRate * 100):F1}% at {rank.AvgExecutionMs:F2}ms avg execution time.",
                Score: rank.Score,
                Metadata: new Dictionary<string, string>
                {
                    ["agentId"] = rank.AgentId.ToString(),
                    ["totalExecutions"] = rank.Total.ToString(),
                    ["successRate"] = rank.SuccessRate.ToString("F4"),
                    ["avgExecutionMs"] = rank.AvgExecutionMs.ToString("F2")
                },
                DiscoveredAtUtc: DateTimeOffset.UtcNow));
        }

        foreach (OperationalPattern pattern in patterns)
        {
            var memoryRecord = new MemoryRecord(
                Id: Guid.NewGuid(),
                MemoryType: "intelligence-pattern",
                Scope: $"objective:{objectiveId}",
                Content: pattern.Description,
                Metadata: new Dictionary<string, string>(pattern.Metadata)
                {
                    ["patternId"] = pattern.Id.ToString(),
                    ["patternType"] = pattern.PatternType,
                    ["title"] = pattern.Title,
                    ["score"] = pattern.Score.ToString("F4")
                },
                CreatedAtUtc: pattern.DiscoveredAtUtc,
                ExpiresAtUtc: null);

            await _memoryStore.SaveAsync(memoryRecord, cancellationToken);
        }

        return patterns;
    }
}
