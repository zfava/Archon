using ArchonAI.Core.Models.ProofAnalytics;

namespace ArchonAI.Core.Interfaces;

public interface IProofAnalyticsService
{
    /// <summary>Record a proof event in the decision-to-outcome lineage.</summary>
    Task<ProofEvent> RecordEventAsync(ProofEvent proofEvent, CancellationToken ct = default);

    /// <summary>Get the full proof timeline for a decision.</summary>
    Task<ProofTimeline?> GetTimelineAsync(Guid decisionId, CancellationToken ct = default);

    /// <summary>Get proof timeline for a workflow.</summary>
    Task<IReadOnlyList<ProofTimeline>> GetWorkflowTimelinesAsync(Guid workflowId, CancellationToken ct = default);

    /// <summary>Get predicted vs actual summary for a tenant.</summary>
    Task<PredictedVsActualSummary> GetPredictedVsActualAsync(
        Guid tenantId, string? domain = null, int limit = 50, CancellationToken ct = default);

    /// <summary>Get approval-to-execution conversion metrics.</summary>
    Task<ApprovalConversionSummary> GetApprovalConversionAsync(
        Guid tenantId, CancellationToken ct = default);

    /// <summary>Get execution success/failure trends.</summary>
    Task<ExecutionTrendSummary> GetExecutionTrendsAsync(
        Guid tenantId, int bucketCount = 10, CancellationToken ct = default);

    /// <summary>Get override/reversal rate analytics.</summary>
    Task<OverrideRateSummary> GetOverrideRatesAsync(
        Guid tenantId, CancellationToken ct = default);

    /// <summary>Get trust analytics by action type.</summary>
    Task<TrustAnalyticsSummary> GetTrustAnalyticsAsync(
        Guid tenantId, CancellationToken ct = default);

    /// <summary>Get full proof dashboard.</summary>
    Task<ProofDashboard> GetDashboardAsync(
        Guid tenantId, string? domain = null, CancellationToken ct = default);
}
