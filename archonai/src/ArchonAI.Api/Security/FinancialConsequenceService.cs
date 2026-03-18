using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Decisions;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Api.Security;

public sealed class FinancialConsequenceService : IFinancialConsequenceService
{
    private readonly ConcurrentDictionary<Guid, FinancialConsequence> _store = new();
    private readonly IEventBus _eventBus;
    private readonly ILogger<FinancialConsequenceService> _logger;

    public FinancialConsequenceService(IEventBus eventBus, ILogger<FinancialConsequenceService> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<FinancialConsequence> AttachAsync(FinancialConsequence consequence, CancellationToken ct = default)
    {
        _store[consequence.DecisionId] = consequence;

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "decision.financial_consequence.attached",
            "FinancialConsequenceService",
            consequence.DecisionId,
            new Dictionary<string, string>
            {
                ["decisionId"] = consequence.DecisionId.ToString(),
                ["tenantId"] = consequence.TenantId.ToString(),
                ["hasRevenueImpact"] = (consequence.ExpectedRevenueImpactLow.HasValue || consequence.ExpectedRevenueImpactHigh.HasValue).ToString(),
                ["hasCostImpact"] = (consequence.ExpectedCostImpactLow.HasValue || consequence.ExpectedCostImpactHigh.HasValue).ToString(),
                ["hasRoi"] = (consequence.RoiEstimateLow.HasValue || consequence.RoiEstimateHigh.HasValue).ToString(),
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation(
            "Financial consequence attached to decision {DecisionId} (tenant={TenantId})",
            consequence.DecisionId, consequence.TenantId);

        return consequence;
    }

    public Task<FinancialConsequence?> GetByDecisionAsync(Guid decisionId, CancellationToken ct = default)
    {
        _store.TryGetValue(decisionId, out var consequence);
        return Task.FromResult(consequence);
    }

    public async Task<FinancialConsequence?> UpdateAsync(Guid decisionId, FinancialConsequence consequence, CancellationToken ct = default)
    {
        if (!_store.ContainsKey(decisionId))
            return null;

        var updated = consequence with
        {
            DecisionId = decisionId,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        _store[decisionId] = updated;

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "decision.financial_consequence.updated",
            "FinancialConsequenceService",
            decisionId,
            new Dictionary<string, string>
            {
                ["decisionId"] = decisionId.ToString(),
                ["tenantId"] = updated.TenantId.ToString(),
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation(
            "Financial consequence updated for decision {DecisionId}",
            decisionId);

        return updated;
    }
}
