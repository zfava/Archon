using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Evaluation;
using ArchonAI.Core.Models.Knowledge;
using ArchonAI.Core.Models.Simulation;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Reasoner;

/// <summary>
/// Evaluates outcomes of executed strategies by comparing StrategySimulationResult
/// (expected) against TaskGraphExecutionResult (actual). Computes success metrics,
/// generates insights and recommendations, and persists evaluations in the
/// KnowledgeGraph for future calibration.
/// </summary>
public sealed class OutcomeEvaluator : IOutcomeEvaluator
{
    private readonly IKnowledgeGraphStore _knowledgeStore;
    private readonly IPlanningFeedbackStore _feedbackStore;
    private readonly IEventBus _eventBus;
    private readonly ILogger<OutcomeEvaluator> _logger;

    public OutcomeEvaluator(
        IKnowledgeGraphStore knowledgeStore,
        IPlanningFeedbackStore feedbackStore,
        IEventBus eventBus,
        ILogger<OutcomeEvaluator> logger)
    {
        _knowledgeStore = knowledgeStore;
        _feedbackStore = feedbackStore;
        _eventBus = eventBus;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  Core evaluation
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<OutcomeEvaluationResult> EvaluateAsync(
        StrategySimulationResult expectedSimulation,
        TaskGraphExecutionResult actualResult,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var expectedNodeMap = expectedSimulation.NodeResults
            .ToDictionary(n => n.NodeId);

        // ── Per-node evaluation ──────────────────────────────────
        var nodeEvaluations = new List<NodeEvaluationResult>();

        foreach (var actualNode in actualResult.NodeResults)
        {
            expectedNodeMap.TryGetValue(actualNode.NodeId, out var expectedNode);

            double predictedSuccessProb = expectedNode?.SuccessProbability ?? 0;
            double predictedDuration = expectedNode?.EstimatedDurationHours ?? 0;
            decimal predictedCost = expectedNode?.EstimatedCost ?? 0;
            bool wasOnCriticalPath = expectedNode?.IsOnCriticalPath ?? false;

            string assessment = AssessNode(
                actualNode.Succeeded, predictedSuccessProb,
                actualNode.DurationHours, predictedDuration,
                actualNode.Cost, predictedCost);

            nodeEvaluations.Add(new NodeEvaluationResult(
                NodeId: actualNode.NodeId,
                NodeName: actualNode.NodeName,
                AgentType: actualNode.AgentType,
                PredictedSuccessProbability: predictedSuccessProb,
                ActualSuccess: actualNode.Succeeded,
                PredictedDurationHours: predictedDuration,
                ActualDurationHours: actualNode.DurationHours,
                PredictedCost: predictedCost,
                ActualCost: actualNode.Cost,
                WasOnCriticalPath: wasOnCriticalPath,
                Assessment: assessment));
        }

        // ── Expected vs Actual comparison ────────────────────────
        var expected = expectedSimulation.ExpectedOutcome;
        double durationDeviation = expectedSimulation.EstimatedTotalDurationHours > 0
            ? ((actualResult.TotalDurationHours - expectedSimulation.EstimatedTotalDurationHours)
               / expectedSimulation.EstimatedTotalDurationHours) * 100
            : 0;

        double costDeviation = expectedSimulation.EstimatedTotalCost > 0
            ? (double)((actualResult.TotalCost - expectedSimulation.EstimatedTotalCost)
               / expectedSimulation.EstimatedTotalCost) * 100
            : 0;

        bool riskPredictionCorrect =
            (expectedSimulation.RiskScore >= 0.5 && !actualResult.OverallSuccess) ||
            (expectedSimulation.RiskScore < 0.5 && actualResult.OverallSuccess);

        var comparison = new ExpectedVsActual(
            ExpectedSuccessProbability: expected.OverallSuccessProbability,
            ActualSuccess: actualResult.OverallSuccess,
            ExpectedDurationHours: expectedSimulation.EstimatedTotalDurationHours,
            ActualDurationHours: actualResult.TotalDurationHours,
            DurationDeviationPercent: Math.Round(durationDeviation, 2),
            ExpectedCost: expectedSimulation.EstimatedTotalCost,
            ActualCost: actualResult.TotalCost,
            CostDeviationPercent: Math.Round(costDeviation, 2),
            ExpectedRiskScore: expectedSimulation.RiskScore,
            RiskPredictionCorrect: riskPredictionCorrect);

        // ── Success metrics ──────────────────────────────────────
        int succeededNodes = actualResult.NodeResults.Count(n => n.Succeeded);
        int failedNodes = actualResult.NodeResults.Count - succeededNodes;
        double successRate = actualResult.NodeResults.Count > 0
            ? succeededNodes / (double)actualResult.NodeResults.Count
            : 0;

        double accuracyScore = ComputeAccuracyScore(nodeEvaluations);
        double durationAccuracy = 1.0 - Math.Min(1.0, Math.Abs(durationDeviation) / 100);
        double costAccuracy = 1.0 - Math.Min(1.0, Math.Abs(costDeviation) / 100);
        double riskPredictionAccuracy = riskPredictionCorrect ? 1.0 : 0.0;

        double overallScore = (successRate * 0.30)
                            + (accuracyScore * 0.25)
                            + (durationAccuracy * 0.20)
                            + (costAccuracy * 0.15)
                            + (riskPredictionAccuracy * 0.10);

        var successMetrics = new SuccessMetrics(
            SuccessRate: Math.Round(successRate, 4),
            AccuracyScore: Math.Round(accuracyScore, 4),
            DurationAccuracy: Math.Round(durationAccuracy, 4),
            CostAccuracy: Math.Round(costAccuracy, 4),
            RiskPredictionAccuracy: riskPredictionAccuracy,
            TotalNodes: actualResult.NodeResults.Count,
            SucceededNodes: succeededNodes,
            FailedNodes: failedNodes,
            OverallScore: Math.Round(overallScore, 4));

        // ── Insights & recommendations ───────────────────────────
        var insights = GenerateInsights(comparison, nodeEvaluations, successMetrics);
        var recommendations = GenerateRecommendations(comparison, nodeEvaluations, actualResult.Strategy);

        string overallAssessment = overallScore >= 0.8 ? "excellent"
            : overallScore >= 0.6 ? "good"
            : overallScore >= 0.4 ? "fair"
            : "poor";

        var evaluationId = Guid.NewGuid();

        var result = new OutcomeEvaluationResult(
            EvaluationId: evaluationId,
            GraphId: actualResult.GraphId,
            GoalId: actualResult.GoalId,
            Strategy: actualResult.Strategy,
            SuccessMetrics: successMetrics,
            Comparison: comparison,
            NodeEvaluations: nodeEvaluations,
            Insights: insights,
            Recommendations: recommendations,
            OverallAssessment: overallAssessment,
            EvaluatedAtUtc: DateTimeOffset.UtcNow);

        // ── Store in KnowledgeGraph ──────────────────────────────
        await StoreInKnowledgeGraphAsync(result, cancellationToken);

        // ── Write planner feedback ───────────────────────────────
        await _feedbackStore.AddAsync(new PlanningFeedback(
            Strategy: actualResult.Strategy,
            Capability: "outcome-evaluation",
            WasSuccessful: actualResult.OverallSuccess,
            Rationale: $"Outcome evaluation score {overallScore:F3} ({overallAssessment}). " +
                       $"Success rate {successRate:P0}, prediction accuracy {accuracyScore:P0}.",
            RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        // ── Publish event ────────────────────────────────────────
        await _eventBus.PublishAsync(new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: "reasoner.outcome.evaluated",
            Source: nameof(OutcomeEvaluator),
            CorrelationId: actualResult.GoalId,
            Payload: new Dictionary<string, string>
            {
                ["evaluationId"] = evaluationId.ToString(),
                ["goalId"] = actualResult.GoalId.ToString(),
                ["graphId"] = actualResult.GraphId.ToString(),
                ["strategy"] = actualResult.Strategy,
                ["overallScore"] = overallScore.ToString("F4"),
                ["assessment"] = overallAssessment,
                ["successRate"] = successRate.ToString("F4"),
                ["actualSuccess"] = actualResult.OverallSuccess.ToString()
            },
            OccurredAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        _logger.LogInformation(
            "Outcome evaluation for goal {GoalId}, graph {GraphId}, strategy '{Strategy}': " +
            "score={OverallScore:F3} ({Assessment}), success rate={SuccessRate:P0}, " +
            "duration deviation={DurationDev:F1}%, cost deviation={CostDev:F1}%",
            actualResult.GoalId, actualResult.GraphId, actualResult.Strategy,
            overallScore, overallAssessment, successRate, durationDeviation, costDeviation);

        return result;
    }

    // ══════════════════════════════════════════════════════════════
    //  KnowledgeGraph retrieval
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<IReadOnlyList<OutcomeEvaluationResult>> GetEvaluationsForGoalAsync(
        Guid goalId,
        CancellationToken cancellationToken = default)
    {
        var relationships = await _knowledgeStore.QueryRelationshipsAsync(
            fromNodeId: $"goal:{goalId}",
            relationshipType: "has_evaluation",
            cancellationToken: cancellationToken);

        var results = new List<OutcomeEvaluationResult>();
        foreach (var rel in relationships)
        {
            var nodes = await _knowledgeStore.QueryRelatedNodesAsync(
                rel.ToNodeId, cancellationToken: cancellationToken);

            var evalNode = nodes.FirstOrDefault(n => n.NodeType == "outcome_evaluation");
            if (evalNode is not null)
            {
                var evaluation = DeserializeEvaluation(evalNode);
                if (evaluation is not null)
                    results.Add(evaluation);
            }
        }

        return results;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<OutcomeEvaluationResult>> GetEvaluationsForStrategyAsync(
        string strategy,
        CancellationToken cancellationToken = default)
    {
        var relationships = await _knowledgeStore.QueryRelationshipsAsync(
            fromNodeId: $"strategy:{strategy}",
            relationshipType: "has_evaluation",
            cancellationToken: cancellationToken);

        var results = new List<OutcomeEvaluationResult>();
        foreach (var rel in relationships)
        {
            var nodes = await _knowledgeStore.QueryRelatedNodesAsync(
                rel.ToNodeId, cancellationToken: cancellationToken);

            var evalNode = nodes.FirstOrDefault(n => n.NodeType == "outcome_evaluation");
            if (evalNode is not null)
            {
                var evaluation = DeserializeEvaluation(evalNode);
                if (evaluation is not null)
                    results.Add(evaluation);
            }
        }

        return results;
    }

    // ══════════════════════════════════════════════════════════════
    //  KnowledgeGraph persistence
    // ══════════════════════════════════════════════════════════════

    private async global::System.Threading.Tasks.Task StoreInKnowledgeGraphAsync(
        OutcomeEvaluationResult evaluation,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        string evalNodeId = $"evaluation:{evaluation.EvaluationId}";

        // Store the evaluation node with serialized metrics
        await _knowledgeStore.UpsertNodeAsync(new KnowledgeNode(
            NodeId: evalNodeId,
            NodeType: "outcome_evaluation",
            DisplayName: $"Evaluation {evaluation.Strategy} ({evaluation.OverallAssessment})",
            Properties: new Dictionary<string, string>
            {
                ["evaluationId"] = evaluation.EvaluationId.ToString(),
                ["graphId"] = evaluation.GraphId.ToString(),
                ["goalId"] = evaluation.GoalId.ToString(),
                ["strategy"] = evaluation.Strategy,
                ["overallScore"] = evaluation.SuccessMetrics.OverallScore.ToString("F4"),
                ["successRate"] = evaluation.SuccessMetrics.SuccessRate.ToString("F4"),
                ["accuracyScore"] = evaluation.SuccessMetrics.AccuracyScore.ToString("F4"),
                ["durationAccuracy"] = evaluation.SuccessMetrics.DurationAccuracy.ToString("F4"),
                ["costAccuracy"] = evaluation.SuccessMetrics.CostAccuracy.ToString("F4"),
                ["riskPredictionAccuracy"] = evaluation.SuccessMetrics.RiskPredictionAccuracy.ToString("F1"),
                ["totalNodes"] = evaluation.SuccessMetrics.TotalNodes.ToString(),
                ["succeededNodes"] = evaluation.SuccessMetrics.SucceededNodes.ToString(),
                ["failedNodes"] = evaluation.SuccessMetrics.FailedNodes.ToString(),
                ["assessment"] = evaluation.OverallAssessment,
                ["actualSuccess"] = evaluation.Comparison.ActualSuccess.ToString(),
                ["expectedSuccessProb"] = evaluation.Comparison.ExpectedSuccessProbability.ToString("F4"),
                ["durationDeviationPercent"] = evaluation.Comparison.DurationDeviationPercent.ToString("F2"),
                ["costDeviationPercent"] = evaluation.Comparison.CostDeviationPercent.ToString("F2"),
                ["evaluatedAtUtc"] = evaluation.EvaluatedAtUtc.ToString("O")
            },
            UpdatedAtUtc: now), cancellationToken);

        // Link goal → evaluation
        await _knowledgeStore.UpsertRelationshipAsync(new KnowledgeRelationship(
            RelationshipId: $"goal:{evaluation.GoalId}->eval:{evaluation.EvaluationId}",
            FromNodeId: $"goal:{evaluation.GoalId}",
            RelationshipType: "has_evaluation",
            ToNodeId: evalNodeId,
            Properties: new Dictionary<string, string>
            {
                ["strategy"] = evaluation.Strategy,
                ["score"] = evaluation.SuccessMetrics.OverallScore.ToString("F4")
            },
            UpdatedAtUtc: now), cancellationToken);

        // Link strategy → evaluation
        await _knowledgeStore.UpsertRelationshipAsync(new KnowledgeRelationship(
            RelationshipId: $"strategy:{evaluation.Strategy}->eval:{evaluation.EvaluationId}",
            FromNodeId: $"strategy:{evaluation.Strategy}",
            RelationshipType: "has_evaluation",
            ToNodeId: evalNodeId,
            Properties: new Dictionary<string, string>
            {
                ["goalId"] = evaluation.GoalId.ToString(),
                ["score"] = evaluation.SuccessMetrics.OverallScore.ToString("F4"),
                ["assessment"] = evaluation.OverallAssessment
            },
            UpdatedAtUtc: now), cancellationToken);

        // Link graph → evaluation
        await _knowledgeStore.UpsertRelationshipAsync(new KnowledgeRelationship(
            RelationshipId: $"graph:{evaluation.GraphId}->eval:{evaluation.EvaluationId}",
            FromNodeId: $"graph:{evaluation.GraphId}",
            RelationshipType: "has_evaluation",
            ToNodeId: evalNodeId,
            Properties: new Dictionary<string, string>
            {
                ["strategy"] = evaluation.Strategy,
                ["score"] = evaluation.SuccessMetrics.OverallScore.ToString("F4")
            },
            UpdatedAtUtc: now), cancellationToken);

        // Store per-node evaluation results as linked nodes
        foreach (var nodeEval in evaluation.NodeEvaluations)
        {
            string nodeEvalId = $"node_eval:{evaluation.EvaluationId}:{nodeEval.NodeId}";

            await _knowledgeStore.UpsertNodeAsync(new KnowledgeNode(
                NodeId: nodeEvalId,
                NodeType: "node_outcome_evaluation",
                DisplayName: $"Node eval: {nodeEval.NodeName}",
                Properties: new Dictionary<string, string>
                {
                    ["nodeId"] = nodeEval.NodeId.ToString(),
                    ["nodeName"] = nodeEval.NodeName,
                    ["agentType"] = nodeEval.AgentType,
                    ["predictedSuccessProb"] = nodeEval.PredictedSuccessProbability.ToString("F4"),
                    ["actualSuccess"] = nodeEval.ActualSuccess.ToString(),
                    ["predictedDuration"] = nodeEval.PredictedDurationHours.ToString("F2"),
                    ["actualDuration"] = nodeEval.ActualDurationHours.ToString("F2"),
                    ["predictedCost"] = nodeEval.PredictedCost.ToString("F4"),
                    ["actualCost"] = nodeEval.ActualCost.ToString("F4"),
                    ["wasOnCriticalPath"] = nodeEval.WasOnCriticalPath.ToString(),
                    ["assessment"] = nodeEval.Assessment
                },
                UpdatedAtUtc: now), cancellationToken);

            await _knowledgeStore.UpsertRelationshipAsync(new KnowledgeRelationship(
                RelationshipId: $"eval:{evaluation.EvaluationId}->node_eval:{nodeEval.NodeId}",
                FromNodeId: evalNodeId,
                RelationshipType: "evaluated_node",
                ToNodeId: nodeEvalId,
                Properties: new Dictionary<string, string>
                {
                    ["assessment"] = nodeEval.Assessment
                },
                UpdatedAtUtc: now), cancellationToken);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Internals
    // ══════════════════════════════════════════════════════════════

    private static double ComputeAccuracyScore(IReadOnlyList<NodeEvaluationResult> nodeEvaluations)
    {
        if (nodeEvaluations.Count == 0) return 0;

        double totalAccuracy = 0;
        foreach (var node in nodeEvaluations)
        {
            // Success prediction accuracy: did we predict correctly?
            double successAccuracy = (node.ActualSuccess && node.PredictedSuccessProbability >= 0.5)
                || (!node.ActualSuccess && node.PredictedSuccessProbability < 0.5)
                ? 1.0
                : 0.0;

            // Duration prediction accuracy
            double durationAccuracy = node.PredictedDurationHours > 0
                ? 1.0 - Math.Min(1.0, Math.Abs(node.ActualDurationHours - node.PredictedDurationHours)
                    / node.PredictedDurationHours)
                : 0.5;

            // Cost prediction accuracy
            double costAccuracy = node.PredictedCost > 0
                ? 1.0 - Math.Min(1.0, (double)Math.Abs(node.ActualCost - node.PredictedCost)
                    / (double)node.PredictedCost)
                : 0.5;

            totalAccuracy += (successAccuracy * 0.5) + (durationAccuracy * 0.3) + (costAccuracy * 0.2);
        }

        return totalAccuracy / nodeEvaluations.Count;
    }

    private static string AssessNode(
        bool actualSuccess, double predictedSuccessProb,
        double actualDuration, double predictedDuration,
        decimal actualCost, decimal predictedCost)
    {
        bool successCorrect = (actualSuccess && predictedSuccessProb >= 0.5)
            || (!actualSuccess && predictedSuccessProb < 0.5);

        double durationDeviation = predictedDuration > 0
            ? Math.Abs(actualDuration - predictedDuration) / predictedDuration
            : 0;

        if (!actualSuccess)
        {
            return successCorrect
                ? "failed_as_predicted"
                : "unexpected_failure";
        }

        if (durationDeviation > 0.5)
            return "succeeded_but_slow";

        if (predictedCost > 0 && actualCost > predictedCost * 1.5m)
            return "succeeded_over_budget";

        return successCorrect ? "succeeded_as_predicted" : "unexpected_success";
    }

    private static List<string> GenerateInsights(
        ExpectedVsActual comparison,
        IReadOnlyList<NodeEvaluationResult> nodeEvaluations,
        SuccessMetrics metrics)
    {
        var insights = new List<string>();

        if (comparison.ActualSuccess && comparison.ExpectedSuccessProbability >= 0.8)
            insights.Add("Strategy performed as expected with high confidence.");
        else if (comparison.ActualSuccess && comparison.ExpectedSuccessProbability < 0.5)
            insights.Add("Strategy succeeded despite low predicted probability — model may be too conservative.");
        else if (!comparison.ActualSuccess && comparison.ExpectedSuccessProbability >= 0.8)
            insights.Add("Strategy failed despite high predicted success — model may be too optimistic.");

        if (Math.Abs(comparison.DurationDeviationPercent) > 30)
        {
            string direction = comparison.DurationDeviationPercent > 0 ? "longer" : "shorter";
            insights.Add($"Execution took {Math.Abs(comparison.DurationDeviationPercent):F0}% {direction} than predicted.");
        }

        if (Math.Abs(comparison.CostDeviationPercent) > 25)
        {
            string direction = comparison.CostDeviationPercent > 0 ? "more" : "less";
            insights.Add($"Execution cost {Math.Abs(comparison.CostDeviationPercent):F0}% {direction} than predicted.");
        }

        var unexpectedFailures = nodeEvaluations
            .Where(n => !n.ActualSuccess && n.PredictedSuccessProbability >= 0.8)
            .ToList();
        if (unexpectedFailures.Count > 0)
            insights.Add($"{unexpectedFailures.Count} node(s) failed unexpectedly despite high predicted success: " +
                         string.Join(", ", unexpectedFailures.Select(n => n.NodeName)));

        var criticalPathFailures = nodeEvaluations
            .Where(n => n.WasOnCriticalPath && !n.ActualSuccess)
            .ToList();
        if (criticalPathFailures.Count > 0)
            insights.Add($"{criticalPathFailures.Count} critical-path node(s) failed: " +
                         string.Join(", ", criticalPathFailures.Select(n => n.NodeName)));

        if (metrics.AccuracyScore >= 0.85)
            insights.Add("Simulation predictions are well-calibrated.");
        else if (metrics.AccuracyScore < 0.5)
            insights.Add("Simulation predictions have low accuracy — recalibration recommended.");

        return insights;
    }

    private static List<string> GenerateRecommendations(
        ExpectedVsActual comparison,
        IReadOnlyList<NodeEvaluationResult> nodeEvaluations,
        string strategy)
    {
        var recommendations = new List<string>();

        if (!comparison.ActualSuccess)
        {
            recommendations.Add($"Consider switching from '{strategy}' to 'safe-mode' for similar goals.");

            var failedAgentTypes = nodeEvaluations
                .Where(n => !n.ActualSuccess)
                .Select(n => n.AgentType)
                .Distinct()
                .ToList();
            if (failedAgentTypes.Count > 0)
                recommendations.Add($"Investigate reliability of agent types: {string.Join(", ", failedAgentTypes)}.");
        }

        if (comparison.DurationDeviationPercent > 50)
            recommendations.Add("Duration estimates are significantly under-predicted. Increase base duration estimates.");
        else if (comparison.DurationDeviationPercent < -30)
            recommendations.Add("Duration estimates are over-predicted. Consider reducing base duration estimates.");

        if (comparison.CostDeviationPercent > 50)
            recommendations.Add("Cost estimates are significantly under-predicted. Revise cost-per-hour baselines.");

        if (!comparison.RiskPredictionCorrect)
            recommendations.Add("Risk prediction was incorrect. Recalibrate risk thresholds and scoring weights.");

        var slowCriticalNodes = nodeEvaluations
            .Where(n => n.WasOnCriticalPath && n.ActualDurationHours > n.PredictedDurationHours * 1.5)
            .ToList();
        if (slowCriticalNodes.Count > 0)
            recommendations.Add($"Critical-path bottleneck(s) detected: {string.Join(", ", slowCriticalNodes.Select(n => n.NodeName))}. " +
                                "Consider parallelizing or optimizing these nodes.");

        return recommendations;
    }

    private static OutcomeEvaluationResult? DeserializeEvaluation(KnowledgeNode node)
    {
        if (!node.Properties.TryGetValue("evaluationId", out var evalIdStr) ||
            !Guid.TryParse(evalIdStr, out var evalId))
            return null;

        node.Properties.TryGetValue("graphId", out var graphIdStr);
        node.Properties.TryGetValue("goalId", out var goalIdStr);
        node.Properties.TryGetValue("strategy", out var strategy);
        node.Properties.TryGetValue("overallScore", out var scoreStr);
        node.Properties.TryGetValue("successRate", out var successRateStr);
        node.Properties.TryGetValue("accuracyScore", out var accuracyStr);
        node.Properties.TryGetValue("durationAccuracy", out var durAccStr);
        node.Properties.TryGetValue("costAccuracy", out var costAccStr);
        node.Properties.TryGetValue("riskPredictionAccuracy", out var riskAccStr);
        node.Properties.TryGetValue("totalNodes", out var totalStr);
        node.Properties.TryGetValue("succeededNodes", out var succStr);
        node.Properties.TryGetValue("failedNodes", out var failStr);
        node.Properties.TryGetValue("assessment", out var assessment);
        node.Properties.TryGetValue("actualSuccess", out var actualSuccessStr);
        node.Properties.TryGetValue("expectedSuccessProb", out var expectedProbStr);
        node.Properties.TryGetValue("durationDeviationPercent", out var durDevStr);
        node.Properties.TryGetValue("costDeviationPercent", out var costDevStr);
        node.Properties.TryGetValue("evaluatedAtUtc", out var evalAtStr);

        _ = Guid.TryParse(graphIdStr, out var graphId);
        _ = Guid.TryParse(goalIdStr, out var goalId);
        _ = double.TryParse(scoreStr, out var score);
        _ = double.TryParse(successRateStr, out var successRate);
        _ = double.TryParse(accuracyStr, out var accuracy);
        _ = double.TryParse(durAccStr, out var durAcc);
        _ = double.TryParse(costAccStr, out var costAcc);
        _ = double.TryParse(riskAccStr, out var riskAcc);
        _ = int.TryParse(totalStr, out var total);
        _ = int.TryParse(succStr, out var succ);
        _ = int.TryParse(failStr, out var fail);
        _ = bool.TryParse(actualSuccessStr, out var actualSuccess);
        _ = double.TryParse(expectedProbStr, out var expectedProb);
        _ = double.TryParse(durDevStr, out var durDev);
        _ = double.TryParse(costDevStr, out var costDev);
        _ = DateTimeOffset.TryParse(evalAtStr, out var evalAt);

        return new OutcomeEvaluationResult(
            EvaluationId: evalId,
            GraphId: graphId,
            GoalId: goalId,
            Strategy: strategy ?? "unknown",
            SuccessMetrics: new SuccessMetrics(successRate, accuracy, durAcc, costAcc, riskAcc, total, succ, fail, score),
            Comparison: new ExpectedVsActual(expectedProb, actualSuccess, 0, 0, durDev, 0, 0, costDev, 0, false),
            NodeEvaluations: [],
            Insights: [],
            Recommendations: [],
            OverallAssessment: assessment ?? "unknown",
            EvaluatedAtUtc: evalAt);
    }
}
