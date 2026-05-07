using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using CoreTask = ArchonAI.Core.Models.Task;
using ArchonAI.Core.Models.Evaluation;
using ArchonAI.Core.Models.Knowledge;

namespace ArchonAI.Evaluation;

/// <summary>
/// Evaluates execution outcomes, detects failure patterns, and records agent performance metrics.
/// </summary>
public sealed class EvaluationEngine : IEvaluationEngine
{
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<ExecutionResult>> _historyByAgent = new();
    private readonly IKnowledgeGraphEngine _knowledgeGraphEngine;
    private readonly IPatternDiscoveryEngine _patternDiscoveryEngine;

    public EvaluationEngine(
        IKnowledgeGraphEngine knowledgeGraphEngine,
        IPatternDiscoveryEngine patternDiscoveryEngine)
    {
        _knowledgeGraphEngine = knowledgeGraphEngine;
        _patternDiscoveryEngine = patternDiscoveryEngine;
    }

    public async global::System.Threading.Tasks.Task<EvaluationReport> EvaluateAsync(
        Agent agent,
        CoreTask task,
        ExecutionResult result,
        double latencyMs,
        decimal estimatedCost,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var history = _historyByAgent.GetOrAdd(agent.Id, _ => new ConcurrentQueue<ExecutionResult>());
        history.Enqueue(result);

        while (history.Count > 200 && history.TryDequeue(out _))
        {
            // Maintain bounded history window per agent.
        }

        await _knowledgeGraphEngine.LinkAgentToTaskAsync(agent.Id, task.Id, cancellationToken);
        IReadOnlyList<KnowledgeNode> relatedSystems = await _knowledgeGraphEngine.GetSystemsForTaskAsync(task.Id, cancellationToken);
        IReadOnlyList<KnowledgeNode> agentTasks = await _knowledgeGraphEngine.GetTasksForAgentAsync(agent.Id, cancellationToken);

        IReadOnlyList<FailurePattern> patterns = DetectFailurePatterns(history, relatedSystems, agentTasks);
        await _patternDiscoveryEngine.DiscoverObjectivePatternsAsync(task.ObjectiveId, cancellationToken);
        double score = ScoreResult(result, latencyMs, estimatedCost, patterns);

        var report = new EvaluationReport(
            TaskId: task.Id,
            AgentId: agent.Id,
            Score: score,
            IsFailure: !result.IsSuccess,
            FailurePatterns: patterns,
            LatencyMs: Math.Max(0, latencyMs),
            Cost: Math.Max(0, estimatedCost),
            EvaluatedAtUtc: DateTimeOffset.UtcNow);

        return report;
    }

    private static IReadOnlyList<FailurePattern> DetectFailurePatterns(
        IEnumerable<ExecutionResult> history,
        IReadOnlyList<KnowledgeNode> relatedSystems,
        IReadOnlyList<KnowledgeNode> agentTasks)
    {
        var failures = history.Where(r => !r.IsSuccess).ToArray();
        if (failures.Length == 0)
        {
            return Array.Empty<FailurePattern>();
        }

        int repeatedErrorFrequency = failures
            .SelectMany(f => f.Errors)
            .GroupBy(e => e, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Count())
            .DefaultIfEmpty(0)
            .Max();

        int recentFailureBurst = failures
            .TakeLast(10)
            .Count();

        var patterns = new List<FailurePattern>();

        if (repeatedErrorFrequency >= 3)
        {
            patterns.Add(new FailurePattern("repeated-error-signature", repeatedErrorFrequency, "high"));
        }

        if (recentFailureBurst >= 5)
        {
            patterns.Add(new FailurePattern("recent-failure-burst", recentFailureBurst, "high"));
        }
        else if (recentFailureBurst >= 2)
        {
            patterns.Add(new FailurePattern("intermittent-failures", recentFailureBurst, "medium"));
        }

        if (relatedSystems.Count == 0)
        {
            patterns.Add(new FailurePattern("missing-system-linkage", 1, "medium"));
        }

        if (agentTasks.Count < 3)
        {
            patterns.Add(new FailurePattern("low-agent-task-history", agentTasks.Count, "low"));
        }

        return patterns;
    }

    private static double ScoreResult(ExecutionResult result, double latencyMs, decimal cost, IReadOnlyList<FailurePattern> patterns)
    {
        double score = result.IsSuccess ? 100 : 40;

        score -= Math.Min(30, Math.Max(0, latencyMs) / 50.0);
        score -= Math.Min(20, (double)Math.Max(0, cost) * 10.0);

        score -= patterns.Sum(pattern => pattern.Severity switch
        {
            "high" => 15,
            "medium" => 8,
            _ => 4
        });

        return Math.Clamp(score, 0, 100);
    }
}
