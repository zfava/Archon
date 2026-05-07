using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Scenario;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Api.Security;

public sealed class ScenarioService : IScenarioService
{
    private readonly ConcurrentDictionary<Guid, Scenario> _scenarios = new();
    private readonly IEventBus _eventBus;
    private readonly ILogger<ScenarioService> _logger;

    public ScenarioService(IEventBus eventBus, ILogger<ScenarioService> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<Scenario> CreateScenarioAsync(Scenario scenario, CancellationToken ct = default)
    {
        _scenarios[scenario.Id] = scenario;

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(), "scenario.created", "ScenarioService",
            scenario.Id,
            new Dictionary<string, string>
            {
                ["scenarioId"] = scenario.Id.ToString(),
                ["tenantId"] = scenario.TenantId.ToString(),
                ["type"] = scenario.Type.ToString(),
                ["title"] = scenario.Title,
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation("Scenario created: {ScenarioId} type={Type} title={Title}",
            scenario.Id, scenario.Type, scenario.Title);
        return scenario;
    }

    public Task<Scenario?> UpdateAssumptionsAsync(
        Guid scenarioId, Guid tenantId,
        IReadOnlyList<ScenarioAssumption> assumptions,
        CancellationToken ct = default)
    {
        if (!_scenarios.TryGetValue(scenarioId, out var existing) || existing.TenantId != tenantId)
            return Task.FromResult<Scenario?>(null);

        // Recompute projected effects based on new assumptions
        var effects = DeriveProjectedEffects(existing, assumptions);

        var updated = existing with
        {
            Assumptions = assumptions,
            ProjectedEffects = effects,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        _scenarios[scenarioId] = updated;
        return Task.FromResult<Scenario?>(updated);
    }

    public Task<Scenario?> GetScenarioAsync(Guid scenarioId, Guid tenantId, CancellationToken ct = default)
    {
        _scenarios.TryGetValue(scenarioId, out var scenario);
        if (scenario is not null && scenario.TenantId != tenantId)
            return Task.FromResult<Scenario?>(null);
        return Task.FromResult(scenario);
    }

    public Task<IReadOnlyList<Scenario>> ListScenariosAsync(
        Guid tenantId, ScenarioType? type = null,
        ScenarioStatus? status = null, CancellationToken ct = default)
    {
        var q = _scenarios.Values.Where(s => s.TenantId == tenantId);
        if (type.HasValue) q = q.Where(s => s.Type == type.Value);
        if (status.HasValue) q = q.Where(s => s.Status == status.Value);
        IReadOnlyList<Scenario> result = q
            .OrderByDescending(s => s.UpdatedAtUtc)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<ScenarioComparison> CompareScenariosAsync(
        IReadOnlyList<Guid> scenarioIds, Guid tenantId,
        CancellationToken ct = default)
    {
        var scenarios = scenarioIds
            .Select(id => _scenarios.TryGetValue(id, out var s) && s.TenantId == tenantId ? s : null)
            .Where(s => s is not null)
            .Cast<Scenario>()
            .ToList();

        // Build comparison axes from the union of all projected-effect metrics
        var allMetrics = scenarios
            .SelectMany(s => s.ProjectedEffects)
            .Select(e => (e.Metric, e.Unit))
            .Distinct()
            .ToList();

        var axes = allMetrics.Select(m =>
        {
            var values = new Dictionary<Guid, double?>();
            foreach (var s in scenarios)
            {
                var effect = s.ProjectedEffects.FirstOrDefault(e => e.Metric == m.Metric);
                values[s.Id] = effect?.ProjectedValue;
            }
            return new ScenarioComparisonAxis(m.Metric, m.Unit,
                values as IReadOnlyDictionary<Guid, double?>);
        }).ToList();

        // Mark participating scenarios as Compared
        foreach (var s in scenarios)
        {
            if (s.Status != ScenarioStatus.Compared)
                _scenarios[s.Id] = s with { Status = ScenarioStatus.Compared, UpdatedAtUtc = DateTimeOffset.UtcNow };
        }

        return Task.FromResult(new ScenarioComparison(
            scenarioIds, axes, DateTimeOffset.UtcNow));
    }

    public Task<bool> DeleteScenarioAsync(Guid scenarioId, Guid tenantId, CancellationToken ct = default)
    {
        if (!_scenarios.TryGetValue(scenarioId, out var s) || s.TenantId != tenantId)
            return Task.FromResult(false);
        return Task.FromResult(_scenarios.TryRemove(scenarioId, out _));
    }

    // ── Internal projection logic ──────────────────────────────
    // Deterministic, bounded effect derivation from assumptions.
    // This is NOT an AI forecast — it maps assumption deltas to
    // structured effect descriptions the operator can review.

    internal static IReadOnlyList<ProjectedEffect> DeriveProjectedEffects(
        Scenario scenario,
        IReadOnlyList<ScenarioAssumption> assumptions)
    {
        var effects = new List<ProjectedEffect>();

        foreach (var a in assumptions)
        {
            if (!double.TryParse(a.CurrentValue, out var current) ||
                !double.TryParse(a.ProposedValue, out var proposed))
            {
                // Non-numeric assumption — produce qualitative effect
                effects.Add(new ProjectedEffect(
                    Area: a.Name,
                    Metric: a.Name,
                    BaselineValue: null,
                    ProjectedValue: null,
                    Unit: a.Unit,
                    Direction: a.CurrentValue == a.ProposedValue ? "Unchanged" : "Changed",
                    Confidence: "Low"));
                continue;
            }

            var delta = proposed - current;
            var direction = delta > 0 ? "Increase"
                          : delta < 0 ? "Decrease"
                          : "Unchanged";

            effects.Add(new ProjectedEffect(
                Area: a.Name,
                Metric: a.Name,
                BaselineValue: current,
                ProjectedValue: proposed,
                Unit: a.Unit,
                Direction: direction,
                Confidence: Math.Abs(delta) / Math.Max(Math.Abs(current), 1) < 0.1
                    ? "High" : "Medium"));
        }

        return effects;
    }
}
