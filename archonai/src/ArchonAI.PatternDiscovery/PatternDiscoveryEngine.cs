using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Patterns;
using ArchonAI.Core.Models.Telemetry;
using Microsoft.Extensions.Options;

namespace ArchonAI.PatternDiscovery;

public sealed class PatternDiscoveryEngine : IPatternDiscoveryEngine
{
    private readonly ITaskTelemetryStore _telemetryStore;
    private readonly IMemoryStore _memoryStore;
    private readonly PatternDiscoveryOptions _options;
    private readonly ILearningEngine _learningEngine;
    private readonly IMultiTenantContext _tenantContext;

    public PatternDiscoveryEngine(
        ITaskTelemetryStore telemetryStore,
        IMemoryStore memoryStore,
        ILearningEngine learningEngine,
        IMultiTenantContext tenantContext,
        IOptions<PatternDiscoveryOptions> options)
    {
        _telemetryStore = telemetryStore;
        _memoryStore = memoryStore;
        _learningEngine = learningEngine;
        _tenantContext = tenantContext;
        _options = options.Value;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<OperationalPattern>> DiscoverObjectivePatternsAsync(
        Guid objectiveId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TaskExecutionTelemetry> telemetry = await _telemetryStore.QueryByObjectiveAsync(objectiveId, 2000, cancellationToken);
        if (telemetry.Count == 0)
        {
            return Array.Empty<OperationalPattern>();
        }

        var patterns = new List<OperationalPattern>();

        patterns.AddRange(DetectFailures(objectiveId, telemetry));
        patterns.AddRange(DiscoverWorkflowOptimizationOpportunities(objectiveId, telemetry));
        patterns.AddRange(RankAgentPerformance(objectiveId, telemetry));
        patterns.AddRange(ExtractStrategies(objectiveId, telemetry));

        foreach (OperationalPattern pattern in patterns)
        {
            var record = new MemoryRecord(
                Id: Guid.NewGuid(),
                MemoryType: "intelligence-pattern",
                Scope: $"{_options.IntelligenceScopePrefix}:{objectiveId}",
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

            await _memoryStore.SaveAsync(record, cancellationToken);
        }

        await _learningEngine.IngestPatternsAsync(_tenantContext.CurrentTenantId, patterns, cancellationToken);

        return patterns;
    }

    private IReadOnlyList<OperationalPattern> DetectFailures(Guid objectiveId, IReadOnlyList<TaskExecutionTelemetry> telemetry)
    {
        var patterns = new List<OperationalPattern>();

        var recurringFailure = telemetry
            .Where(entry => !entry.Success && !string.IsNullOrWhiteSpace(entry.ErrorType) && !entry.ErrorType.Equals("none", StringComparison.OrdinalIgnoreCase))
            .GroupBy(entry => entry.ErrorType, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { ErrorType = group.Key, Count = group.Count() })
            .Where(group => group.Count >= _options.MinRecurringFailures)
            .OrderByDescending(group => group.Count)
            .FirstOrDefault();

        if (recurringFailure is not null)
        {
            patterns.Add(new OperationalPattern(
                Id: Guid.NewGuid(),
                ObjectiveId: objectiveId,
                PatternType: "failure-detection",
                Title: "Recurring failure pattern detected",
                Description: $"Error '{recurringFailure.ErrorType}' repeated {recurringFailure.Count} times.",
                Score: recurringFailure.Count,
                Metadata: new Dictionary<string, string>
                {
                    ["errorType"] = recurringFailure.ErrorType,
                    ["occurrences"] = recurringFailure.Count.ToString()
                },
                DiscoveredAtUtc: DateTimeOffset.UtcNow));
        }

        return patterns;
    }

    private IReadOnlyList<OperationalPattern> DiscoverWorkflowOptimizationOpportunities(Guid objectiveId, IReadOnlyList<TaskExecutionTelemetry> telemetry)
    {
        var patterns = new List<OperationalPattern>();

        var candidates = telemetry
            .GroupBy(entry => entry.WorkflowId)
            .Select(group => new
            {
                WorkflowId = group.Key,
                Count = group.Count(),
                AvgLatency = group.Average(item => item.ExecutionTimeMs),
                SuccessRate = group.Count(item => item.Success) / (double)group.Count()
            })
            .Where(item => item.AvgLatency >= _options.HighExecutionLatencyThresholdMs && item.SuccessRate >= 0.7)
            .OrderByDescending(item => item.AvgLatency)
            .ToArray();

        foreach (var candidate in candidates)
        {
            patterns.Add(new OperationalPattern(
                Id: Guid.NewGuid(),
                ObjectiveId: objectiveId,
                PatternType: "workflow-optimization-opportunity",
                Title: "Workflow optimization opportunity identified",
                Description: $"Workflow {candidate.WorkflowId} avg latency {candidate.AvgLatency:F2}ms with success rate {(candidate.SuccessRate * 100):F1}%.",
                Score: candidate.AvgLatency / Math.Max(1, _options.HighExecutionLatencyThresholdMs),
                Metadata: new Dictionary<string, string>
                {
                    ["workflowId"] = candidate.WorkflowId.ToString(),
                    ["sampleSize"] = candidate.Count.ToString(),
                    ["avgLatencyMs"] = candidate.AvgLatency.ToString("F2"),
                    ["successRate"] = candidate.SuccessRate.ToString("F4")
                },
                DiscoveredAtUtc: DateTimeOffset.UtcNow));
        }

        return patterns;
    }

    private IReadOnlyList<OperationalPattern> RankAgentPerformance(Guid objectiveId, IReadOnlyList<TaskExecutionTelemetry> telemetry)
    {
        var ranked = telemetry
            .GroupBy(entry => entry.AgentId)
            .Select(group => new
            {
                AgentId = group.Key,
                Count = group.Count(),
                SuccessRate = group.Count(item => item.Success) / (double)group.Count(),
                AvgLatency = group.Average(item => item.ExecutionTimeMs),
                AvgCost = group.Average(item => item.Cost),
                Score = (group.Count(item => item.Success) / (double)group.Count()) * 100
                        - (group.Average(item => item.ExecutionTimeMs) / 250.0)
                        - (double)group.Average(item => item.Cost) * 10.0
            })
            .Where(item => item.Count >= _options.MinExecutionsForRanking)
            .OrderByDescending(item => item.Score)
            .Select((item, index) => new { Rank = index + 1, item.AgentId, item.Count, item.SuccessRate, item.AvgLatency, item.AvgCost, item.Score })
            .ToArray();

        return ranked
            .Select(item => new OperationalPattern(
                Id: Guid.NewGuid(),
                ObjectiveId: objectiveId,
                PatternType: "agent-performance-ranking",
                Title: $"Agent performance rank #{item.Rank}",
                Description: $"Agent {item.AgentId} rank {item.Rank} with score {item.Score:F2}, success {(item.SuccessRate * 100):F1}% and avg latency {item.AvgLatency:F2}ms.",
                Score: item.Score,
                Metadata: new Dictionary<string, string>
                {
                    ["rank"] = item.Rank.ToString(),
                    ["agentId"] = item.AgentId.ToString(),
                    ["sampleSize"] = item.Count.ToString(),
                    ["successRate"] = item.SuccessRate.ToString("F4"),
                    ["avgLatencyMs"] = item.AvgLatency.ToString("F2"),
                    ["avgCost"] = item.AvgCost.ToString("F4")
                },
                DiscoveredAtUtc: DateTimeOffset.UtcNow))
            .ToArray();
    }

    private IReadOnlyList<OperationalPattern> ExtractStrategies(Guid objectiveId, IReadOnlyList<TaskExecutionTelemetry> telemetry)
    {
        var strategyCandidates = telemetry
            .Where(entry => entry.Success)
            .GroupBy(entry => entry.WorkflowId)
            .Select(group => new
            {
                WorkflowId = group.Key,
                SuccessCount = group.Count(),
                AvgLatency = group.Average(item => item.ExecutionTimeMs),
                AvgCost = group.Average(item => item.Cost)
            })
            .Where(item => item.SuccessCount >= _options.StrategyCandidateMinSuccesses)
            .OrderByDescending(item => item.SuccessCount)
            .ThenBy(item => item.AvgLatency)
            .Take(3)
            .ToArray();

        return strategyCandidates
            .Select(candidate => new OperationalPattern(
                Id: Guid.NewGuid(),
                ObjectiveId: objectiveId,
                PatternType: "strategy-extraction",
                Title: "Reusable strategy extracted",
                Description: $"Workflow {candidate.WorkflowId} appears reusable ({candidate.SuccessCount} successes, {candidate.AvgLatency:F2}ms avg latency).",
                Score: candidate.SuccessCount / Math.Max(1.0, candidate.AvgLatency / 1000.0),
                Metadata: new Dictionary<string, string>
                {
                    ["workflowId"] = candidate.WorkflowId.ToString(),
                    ["successCount"] = candidate.SuccessCount.ToString(),
                    ["avgLatencyMs"] = candidate.AvgLatency.ToString("F2"),
                    ["avgCost"] = candidate.AvgCost.ToString("F4")
                },
                DiscoveredAtUtc: DateTimeOffset.UtcNow))
            .ToArray();
    }
}
