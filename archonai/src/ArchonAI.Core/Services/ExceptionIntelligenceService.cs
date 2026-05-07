using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.ExceptionIntelligence;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Core.Services;

public sealed class ExceptionIntelligenceService : IExceptionIntelligenceService
{
    private readonly ConcurrentDictionary<Guid, OperationalException> _exceptions = new();
    private readonly IEventBus _eventBus;
    private readonly ILogger<ExceptionIntelligenceService> _logger;

    public ExceptionIntelligenceService(IEventBus eventBus, ILogger<ExceptionIntelligenceService> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<OperationalException> RaiseExceptionAsync(
        OperationalException exception, CancellationToken ct = default)
    {
        _exceptions[exception.Id] = exception;

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(), "exception.raised", "ExceptionIntelligenceService",
            exception.Id,
            new Dictionary<string, string>
            {
                ["exceptionId"] = exception.Id.ToString(),
                ["tenantId"] = exception.TenantId.ToString(),
                ["category"] = exception.Category.ToString(),
                ["severity"] = exception.Severity.ToString(),
                ["title"] = exception.Title,
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);

        _logger.LogWarning(
            "Exception raised: {ExceptionId} severity={Severity} category={Category} title={Title}",
            exception.Id, exception.Severity, exception.Category, exception.Title);

        return exception;
    }

    public Task<OperationalException?> GetExceptionAsync(
        Guid exceptionId, Guid tenantId, CancellationToken ct = default)
    {
        _exceptions.TryGetValue(exceptionId, out var ex);
        if (ex is not null && ex.TenantId != tenantId)
            return Task.FromResult<OperationalException?>(null);
        return Task.FromResult(ex);
    }

    public Task<IReadOnlyList<OperationalException>> ListExceptionsAsync(
        Guid tenantId,
        ExceptionSeverity? severity = null,
        ExceptionCategory? category = null,
        ExceptionStatus? status = null,
        string? domain = null,
        CancellationToken ct = default)
    {
        var q = _exceptions.Values.Where(e => e.TenantId == tenantId);
        if (severity.HasValue) q = q.Where(e => e.Severity == severity.Value);
        if (category.HasValue) q = q.Where(e => e.Category == category.Value);
        if (status.HasValue) q = q.Where(e => e.Status == status.Value);
        if (domain is not null) q = q.Where(e => e.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<OperationalException> result = q
            .OrderByDescending(e => ComputePriorityScore(e))
            .ToList();
        return Task.FromResult(result);
    }

    public Task<OperationalException?> UpdateStatusAsync(
        Guid exceptionId, Guid tenantId,
        ExceptionStatus status, string? assignedTo = null,
        CancellationToken ct = default)
    {
        if (!_exceptions.TryGetValue(exceptionId, out var ex) || ex.TenantId != tenantId)
            return Task.FromResult<OperationalException?>(null);

        var now = DateTimeOffset.UtcNow;
        var updated = ex with
        {
            Status = status,
            AssignedTo = assignedTo ?? ex.AssignedTo,
            UpdatedAtUtc = now,
            AcknowledgedAtUtc = status == ExceptionStatus.Acknowledged && ex.AcknowledgedAtUtc is null
                ? now : ex.AcknowledgedAtUtc,
            ResolvedAtUtc = status == ExceptionStatus.Resolved && ex.ResolvedAtUtc is null
                ? now : ex.ResolvedAtUtc,
        };
        _exceptions[exceptionId] = updated;
        return Task.FromResult<OperationalException?>(updated);
    }

    public Task<OperationalException?> SetRecommendedActionAsync(
        Guid exceptionId, Guid tenantId,
        RecommendedAction action, CancellationToken ct = default)
    {
        if (!_exceptions.TryGetValue(exceptionId, out var ex) || ex.TenantId != tenantId)
            return Task.FromResult<OperationalException?>(null);

        var updated = ex with
        {
            RecommendedAction = action,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        _exceptions[exceptionId] = updated;
        return Task.FromResult<OperationalException?>(updated);
    }

    public Task<ExceptionQueueSummary> GetQueueSummaryAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var open = _exceptions.Values
            .Where(e => e.TenantId == tenantId && e.Status != ExceptionStatus.Resolved && e.Status != ExceptionStatus.Dismissed)
            .ToList();

        var byCategory = open
            .GroupBy(e => e.Category.ToString())
            .ToDictionary(g => g.Key, g => g.Count())
            as IReadOnlyDictionary<string, int>;

        return Task.FromResult(new ExceptionQueueSummary(
            TotalOpen: open.Count,
            Critical: open.Count(e => e.Severity == ExceptionSeverity.Critical),
            High: open.Count(e => e.Severity == ExceptionSeverity.High),
            Warning: open.Count(e => e.Severity == ExceptionSeverity.Warning),
            TotalEconomicExposure: open.Sum(e => e.EconomicImpactEstimate),
            ByCategory: byCategory,
            GeneratedAtUtc: DateTimeOffset.UtcNow));
    }

    public Task<IReadOnlyList<ExceptionPriorityScore>> GetPrioritizedQueueAsync(
        Guid tenantId, int limit = 20, CancellationToken ct = default)
    {
        var open = _exceptions.Values
            .Where(e => e.TenantId == tenantId && e.Status != ExceptionStatus.Resolved && e.Status != ExceptionStatus.Dismissed)
            .Select(e => new ExceptionPriorityScore(
                e.Id,
                ComputePriorityScore(e),
                FormatBreakdown(e)))
            .OrderByDescending(s => s.Score)
            .Take(limit)
            .ToList();

        IReadOnlyList<ExceptionPriorityScore> result = open;
        return Task.FromResult(result);
    }

    // ── Priority scoring ───────────────────────────────────────
    // Composite score: severity weight × urgency × (1 + economic normalized) × confidence
    // Higher = needs attention first.

    internal static double ComputePriorityScore(OperationalException ex)
    {
        double severityWeight = ex.Severity switch
        {
            ExceptionSeverity.Critical => 4.0,
            ExceptionSeverity.High => 3.0,
            ExceptionSeverity.Warning => 2.0,
            ExceptionSeverity.Info => 1.0,
            _ => 1.0,
        };

        double escalationBoost = ex.EscalationLevel switch
        {
            EscalationLevel.Executive => 1.5,
            EscalationLevel.Manager => 1.2,
            EscalationLevel.Operator => 1.0,
            EscalationLevel.None => 1.0,
            _ => 1.0,
        };

        // Normalize economic impact: log scale capped to avoid runaway scores
        var economicFactor = 1.0 + Math.Min(Math.Log10(Math.Max(ex.EconomicImpactEstimate, 1)), 6) / 6.0;

        return severityWeight
             * Math.Max(ex.Urgency, 0.1)
             * economicFactor
             * Math.Max(ex.Confidence, 0.1)
             * escalationBoost;
    }

    private static string FormatBreakdown(OperationalException ex)
    {
        return $"severity={ex.Severity}, urgency={ex.Urgency:F1}, " +
               $"economic=${ex.EconomicImpactEstimate:N0}, " +
               $"confidence={ex.Confidence:F1}, escalation={ex.EscalationLevel}";
    }
}
