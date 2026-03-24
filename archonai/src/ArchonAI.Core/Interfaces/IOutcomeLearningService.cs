using ArchonAI.Core.Models.Decisions;

namespace ArchonAI.Core.Interfaces;

public interface IOutcomeLearningService
{
    /// <summary>
    /// Record the expected outcome for a decision (at prediction time).
    /// </summary>
    Task<OutcomeRecord> RecordExpectedOutcomeAsync(
        Guid decisionId, Guid tenantId,
        string? expectedSummary, decimal? expectedValue,
        double confidenceAtPrediction, string? expectedTimeframe,
        string recordedBy, CancellationToken ct = default);

    /// <summary>
    /// Record the actual outcome and compute variance and recalibration signals.
    /// </summary>
    Task<OutcomeRecord> RecordActualOutcomeAsync(
        Guid decisionId, string? actualSummary, decimal? actualValue,
        string? rootCause, string? notes, string recordedBy,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieve the outcome record for a decision.
    /// </summary>
    Task<OutcomeRecord?> GetOutcomeAsync(Guid decisionId, CancellationToken ct = default);

    /// <summary>
    /// List outcome records for a tenant, optionally filtered by domain.
    /// </summary>
    Task<IReadOnlyList<OutcomeRecord>> ListOutcomesAsync(
        Guid tenantId, int limit = 50, CancellationToken ct = default);

    /// <summary>
    /// Generate a calibration summary for a tenant.
    /// </summary>
    Task<CalibrationSummary> GetCalibrationSummaryAsync(
        Guid tenantId, string? domain = null, CancellationToken ct = default);
}
