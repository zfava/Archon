using ArchonAI.Core.Models.ExceptionIntelligence;

namespace ArchonAI.Core.Interfaces;

public interface IExceptionIntelligenceService
{
    Task<OperationalException> RaiseExceptionAsync(
        OperationalException exception, CancellationToken ct = default);

    Task<OperationalException?> GetExceptionAsync(
        Guid exceptionId, Guid tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<OperationalException>> ListExceptionsAsync(
        Guid tenantId,
        ExceptionSeverity? severity = null,
        ExceptionCategory? category = null,
        ExceptionStatus? status = null,
        string? domain = null,
        CancellationToken ct = default);

    Task<OperationalException?> UpdateStatusAsync(
        Guid exceptionId, Guid tenantId,
        ExceptionStatus status, string? assignedTo = null,
        CancellationToken ct = default);

    Task<OperationalException?> SetRecommendedActionAsync(
        Guid exceptionId, Guid tenantId,
        RecommendedAction action, CancellationToken ct = default);

    Task<ExceptionQueueSummary> GetQueueSummaryAsync(
        Guid tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<ExceptionPriorityScore>> GetPrioritizedQueueAsync(
        Guid tenantId, int limit = 20, CancellationToken ct = default);
}
