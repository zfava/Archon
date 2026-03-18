using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Decisions;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Api.Security;

public sealed class DecisionService : IDecisionService
{
    private readonly ConcurrentDictionary<Guid, DecisionRecord> _decisions = new();
    private readonly ConcurrentDictionary<Guid, List<DecisionLifecycleEvent>> _history = new();
    private readonly IEventBus _eventBus;
    private readonly ILogger<DecisionService> _logger;

    public DecisionService(IEventBus eventBus, ILogger<DecisionService> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<DecisionRecord> CreateAsync(DecisionRecord decision, CancellationToken ct = default)
    {
        _decisions[decision.Id] = decision;

        RecordEvent(decision.Id, "decision.created", decision.CreatedBy,
            $"Decision '{decision.Title}' created in domain '{decision.Domain}'");

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "decision.created",
            "DecisionService",
            decision.Id,
            new Dictionary<string, string>
            {
                ["decisionId"] = decision.Id.ToString(),
                ["tenantId"] = decision.TenantId.ToString(),
                ["domain"] = decision.Domain,
                ["riskLevel"] = decision.RiskLevel.ToString(),
                ["requiresApproval"] = decision.RequiresApproval.ToString(),
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation(
            "Decision {DecisionId} created: {Title} (domain={Domain}, risk={RiskLevel})",
            decision.Id, decision.Title, decision.Domain, decision.RiskLevel);

        return decision;
    }

    public Task<DecisionRecord?> GetAsync(Guid decisionId, CancellationToken ct = default)
    {
        _decisions.TryGetValue(decisionId, out var decision);
        return Task.FromResult(decision);
    }

    public Task<IReadOnlyList<DecisionRecord>> ListAsync(Guid tenantId, string? domain = null,
        DecisionStatus? status = null, int limit = 50, CancellationToken ct = default)
    {
        IEnumerable<DecisionRecord> query = _decisions.Values
            .Where(d => d.TenantId == tenantId);

        if (domain is not null)
            query = query.Where(d => d.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase));

        if (status.HasValue)
            query = query.Where(d => d.Status == status.Value);

        IReadOnlyList<DecisionRecord> result = query
            .OrderByDescending(d => d.UpdatedAtUtc)
            .Take(limit)
            .ToList();

        return Task.FromResult(result);
    }

    public async Task<DecisionRecord?> UpdateStatusAsync(Guid decisionId, DecisionStatus newStatus,
        string actor, string? detail = null, CancellationToken ct = default)
    {
        if (!_decisions.TryGetValue(decisionId, out var existing))
            return null;

        var updated = existing with
        {
            Status = newStatus,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        _decisions[decisionId] = updated;

        RecordEvent(decisionId, $"decision.status.{newStatus.ToString().ToLowerInvariant()}",
            actor, detail ?? $"Status changed to {newStatus}");

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            $"decision.status.{newStatus.ToString().ToLowerInvariant()}",
            "DecisionService",
            decisionId,
            new Dictionary<string, string>
            {
                ["decisionId"] = decisionId.ToString(),
                ["previousStatus"] = existing.Status.ToString(),
                ["newStatus"] = newStatus.ToString(),
                ["actor"] = actor,
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation(
            "Decision {DecisionId} status: {OldStatus} → {NewStatus} by {Actor}",
            decisionId, existing.Status, newStatus, actor);

        return updated;
    }

    public Task<DecisionRecord?> LinkArtifactAsync(Guid decisionId, DecisionLink link,
        CancellationToken ct = default)
    {
        if (!_decisions.TryGetValue(decisionId, out var existing))
            return Task.FromResult<DecisionRecord?>(null);

        var links = existing.LinkedArtifacts.ToList();
        links.Add(link);

        var updated = existing with
        {
            LinkedArtifacts = links,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        _decisions[decisionId] = updated;

        RecordEvent(decisionId, "decision.artifact.linked", "system",
            $"Linked {link.ArtifactType} '{link.ArtifactId}': {link.Description}");

        _logger.LogInformation(
            "Decision {DecisionId} linked to {ArtifactType} {ArtifactId}",
            decisionId, link.ArtifactType, link.ArtifactId);

        return Task.FromResult<DecisionRecord?>(updated);
    }

    public Task<IReadOnlyList<DecisionLifecycleEvent>> GetHistoryAsync(Guid decisionId,
        CancellationToken ct = default)
    {
        _history.TryGetValue(decisionId, out var events);
        IReadOnlyList<DecisionLifecycleEvent> result = events?.OrderBy(e => e.OccurredAtUtc).ToList()
            ?? (IReadOnlyList<DecisionLifecycleEvent>)Array.Empty<DecisionLifecycleEvent>();
        return Task.FromResult(result);
    }

    private void RecordEvent(Guid decisionId, string eventType, string actor, string? detail)
    {
        var events = _history.GetOrAdd(decisionId, _ => new List<DecisionLifecycleEvent>());
        lock (events)
        {
            events.Add(new DecisionLifecycleEvent(
                Guid.NewGuid(), decisionId, eventType, actor, detail, DateTimeOffset.UtcNow));
        }
    }
}
