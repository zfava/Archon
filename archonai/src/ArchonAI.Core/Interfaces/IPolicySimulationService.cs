using ArchonAI.Core.Models.PolicySimulation;

namespace ArchonAI.Core.Interfaces;

public interface IPolicySimulationService
{
    /// <summary>Run a dry-run simulation of an action against the full governance stack. No side effects.</summary>
    Task<SimulationResult> SimulateAsync(
        SimulationRequest request, CancellationToken ct = default);

    /// <summary>Retrieve a previously run simulation result.</summary>
    Task<SimulationResult?> GetAsync(
        Guid simulationId, Guid tenantId, CancellationToken ct = default);

    /// <summary>List simulation results for a tenant.</summary>
    Task<IReadOnlyList<SimulationSummary>> ListAsync(
        Guid tenantId, int limit = 50, CancellationToken ct = default);
}
