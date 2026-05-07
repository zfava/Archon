using ArchonAI.Core.Models.StrategyLibrary;

namespace ArchonAI.Core.Interfaces;

/// <summary>
/// Data access layer for the strategy library.
/// </summary>
public interface IStrategyLibraryRepository
{
    // ── Templates ────────────────────────────────────────────────────

    global::System.Threading.Tasks.Task<StrategyTemplate> UpsertStrategyAsync(
        StrategyTemplate strategy, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<StrategyTemplate?> GetStrategyAsync(
        Guid strategyId, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<StrategyTemplate>> ListStrategiesAsync(
        string? objectiveType, string? tag,
        int offset, int limit, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<bool> RemoveStrategyAsync(
        Guid strategyId, CancellationToken ct = default);

    // ── Execution records ────────────────────────────────────────────

    global::System.Threading.Tasks.Task AddExecutionRecordAsync(
        StrategyExecutionRecord record, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<StrategyExecutionRecord>> GetExecutionRecordsAsync(
        Guid strategyId, int limit = 100, CancellationToken ct = default);
}
