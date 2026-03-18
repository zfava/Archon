using ArchonAI.Core.Models.Scenario;

namespace ArchonAI.Core.Interfaces;

public interface IScenarioService
{
    Task<Scenario> CreateScenarioAsync(Scenario scenario, CancellationToken ct = default);

    Task<Scenario?> UpdateAssumptionsAsync(
        Guid scenarioId, Guid tenantId,
        IReadOnlyList<ScenarioAssumption> assumptions,
        CancellationToken ct = default);

    Task<Scenario?> GetScenarioAsync(Guid scenarioId, Guid tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<Scenario>> ListScenariosAsync(
        Guid tenantId, ScenarioType? type = null,
        ScenarioStatus? status = null, CancellationToken ct = default);

    Task<ScenarioComparison> CompareScenariosAsync(
        IReadOnlyList<Guid> scenarioIds, Guid tenantId,
        CancellationToken ct = default);

    Task<bool> DeleteScenarioAsync(Guid scenarioId, Guid tenantId, CancellationToken ct = default);
}
