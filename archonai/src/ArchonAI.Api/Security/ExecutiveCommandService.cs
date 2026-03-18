using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.ExecutiveCommand;
using ArchonAI.Core.Models.ExceptionIntelligence;
using ArchonAI.Core.Models.Scenario;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Api.Security;

public sealed class ExecutiveCommandService : IExecutiveCommandService
{
    private readonly IExceptionIntelligenceService _exceptions;
    private readonly IGovernanceService _governance;
    private readonly IOutcomeLearningService _outcomes;
    private readonly IOperationalTwinService _twin;
    private readonly ITrustTierService _trustTiers;
    private readonly IScenarioService _scenarios;

    public ExecutiveCommandService(
        IExceptionIntelligenceService exceptions,
        IGovernanceService governance,
        IOutcomeLearningService outcomes,
        IOperationalTwinService twin,
        ITrustTierService trustTiers,
        IScenarioService scenarios)
    {
        _exceptions = exceptions;
        _governance = governance;
        _outcomes = outcomes;
        _twin = twin;
        _trustTiers = trustTiers;
        _scenarios = scenarios;
    }

    public async Task<ExecutiveCommandSummary> GetCommandSummaryAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var tenantStr = tenantId.ToString();

        // Fan out all reads concurrently
        var exSummaryTask = _exceptions.GetQueueSummaryAsync(tenantId, ct);
        var exPrioTask = _exceptions.GetPrioritizedQueueAsync(tenantId, 5, ct);
        var exListTask = _exceptions.ListExceptionsAsync(tenantId, ct: ct);
        var approvalsTask = _governance.ListPendingApprovalsAsync(tenantStr, ct);
        var calibrationTask = _outcomes.GetCalibrationSummaryAsync(tenantId, null, ct);
        var outcomesTask = _outcomes.ListOutcomesAsync(tenantId, 50, ct);
        var twinTask = _twin.GetOverviewAsync(tenantId, ct);
        var tierPoliciesTask = _trustTiers.ListPoliciesAsync(tenantStr, ct);
        var tierMapTask = _trustTiers.GetTierMapAsync(tenantStr, ct);
        var scenariosTask = _scenarios.ListScenariosAsync(tenantId, ct: ct);

        await Task.WhenAll(
            exSummaryTask, exPrioTask, exListTask,
            approvalsTask, calibrationTask, outcomesTask,
            twinTask, tierPoliciesTask, tierMapTask, scenariosTask);

        var exSummary = await exSummaryTask;
        var exPrio = await exPrioTask;
        var exList = await exListTask;
        var approvals = await approvalsTask;
        var calibration = await calibrationTask;
        var outcomes = await outcomesTask;
        var twinOverview = await twinTask;
        var tierPolicies = await tierPoliciesTask;
        var tierMap = await tierMapTask;
        var allScenarios = await scenariosTask;

        // ── Build exception brief ────────────────────────────
        var prioMap = exPrio.ToDictionary(p => p.ExceptionId, p => p.Score);
        var topExceptions = exList
            .Where(e => e.Status != ExceptionStatus.Resolved && e.Status != ExceptionStatus.Dismissed)
            .OrderByDescending(e => prioMap.GetValueOrDefault(e.Id, 0))
            .Take(5)
            .Select(e => new ExceptionHeadline(
                e.Id, e.Severity.ToString(), e.Category.ToString(),
                e.Title, prioMap.GetValueOrDefault(e.Id, 0),
                e.EconomicImpactEstimate,
                e.RecommendedAction?.ActionType))
            .ToList();

        var exceptionBrief = new ExceptionBrief(
            exSummary.TotalOpen, exSummary.Critical, exSummary.High,
            exSummary.TotalEconomicExposure, topExceptions);

        // ── Build approval brief ─────────────────────────────
        var approvalHeadlines = approvals
            .OrderBy(a => a.RequestedAtUtc)
            .Take(10)
            .Select(a => new ApprovalHeadline(
                a.Id, a.ActionType, a.RequestedBy,
                a.Justification, a.RequestedAtUtc))
            .ToList();

        var approvalBrief = new ApprovalBrief(approvals.Count, approvalHeadlines);

        // ── Build calibration brief ──────────────────────────
        var calibrationBrief = new CalibrationBrief(
            calibration.TotalOutcomes,
            calibration.Underperformed,
            calibration.HitRate,
            calibration.MeanVariancePercent,
            calibration.SignalDistribution);

        // ── Build operational brief ──────────────────────────
        var topBottlenecks = twinOverview.ActiveBottlenecks
            .Take(5)
            .Select(b => new BottleneckHeadline(
                b.Id, b.Severity.ToString(), b.Description, b.DetectedAtUtc))
            .ToList();

        var operationalBrief = new OperationalBrief(
            twinOverview.EntityCounts,
            twinOverview.ActiveBottlenecks.Count,
            twinOverview.WarningKpis.Count,
            twinOverview.TotalDependencies,
            topBottlenecks);

        // ── Build trust tier brief ───────────────────────────
        var tierMapStr = tierMap.ToDictionary(
            kv => kv.Key, kv => kv.Value.ToString())
            as IReadOnlyDictionary<string, string>;

        var trustBrief = new TrustTierBrief(tierPolicies.Count, tierMapStr);

        // ── Build scenario brief ─────────────────────────────
        var activeScenarios = allScenarios.Where(s => s.Status == ScenarioStatus.Active).ToList();
        var comparedScenarios = allScenarios.Where(s => s.Status == ScenarioStatus.Compared).ToList();

        var recentScenarios = allScenarios
            .OrderByDescending(s => s.UpdatedAtUtc)
            .Take(5)
            .Select(s => new ScenarioHeadline(
                s.Id, s.Title, s.Type.ToString(), s.Status.ToString(),
                s.Assumptions.Count, s.ProjectedEffects.Count, s.UpdatedAtUtc))
            .ToList();

        var scenarioBrief = new ScenarioBrief(
            activeScenarios.Count, comparedScenarios.Count, recentScenarios);

        // ── Build economic brief ─────────────────────────────
        var driftingOutcomes = outcomes.Count(o =>
            o.Direction == OutcomeDirection.Underperformed);

        var economicBrief = new EconomicBrief(
            exSummary.TotalEconomicExposure,
            approvals.Count,
            driftingOutcomes,
            twinOverview.ActiveBottlenecks.Count);

        return new ExecutiveCommandSummary(
            tenantId,
            exceptionBrief,
            approvalBrief,
            calibrationBrief,
            operationalBrief,
            trustBrief,
            scenarioBrief,
            economicBrief,
            DateTimeOffset.UtcNow);
    }
}
