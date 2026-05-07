using ArchonAI.Core.Models.StrategyLibrary;

namespace ArchonAI.Core.Interfaces;

/// <summary>
/// Service for managing the strategy library — storing, ranking, retrieving,
/// and comparing strategy templates discovered by the intelligence engine.
/// </summary>
public interface IStrategyLibraryService
{
    // ── Store ────────────────────────────────────────────────────────

    global::System.Threading.Tasks.Task<StrategyTemplate> CreateStrategyAsync(
        string name, string description, string objectiveType,
        string workflowTemplate, IReadOnlyDictionary<string, string> successMetrics,
        StrategyResourceUsage resourceUsage, IReadOnlyList<string>? tags = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<StrategyTemplate> UpdateStrategyAsync(
        Guid strategyId, string? description = null,
        string? workflowTemplate = null,
        IReadOnlyDictionary<string, string>? successMetrics = null,
        StrategyResourceUsage? resourceUsage = null,
        IReadOnlyList<string>? tags = null,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<StrategyTemplate?> GetStrategyAsync(
        Guid strategyId, CancellationToken ct = default);

    global::System.Threading.Tasks.Task DeleteStrategyAsync(
        Guid strategyId, CancellationToken ct = default);

    // ── Retrieve ─────────────────────────────────────────────────────

    global::System.Threading.Tasks.Task<IReadOnlyList<StrategyTemplate>> ListStrategiesAsync(
        string? objectiveType = null, string? tag = null,
        int offset = 0, int limit = 50,
        CancellationToken ct = default);

    // ── Rank ─────────────────────────────────────────────────────────

    global::System.Threading.Tasks.Task<StrategyExecutionRecord> RecordExecutionAsync(
        Guid strategyId, bool isSuccess, double latencyMs, double cost,
        IReadOnlyDictionary<string, string>? outcomes = null,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<StrategyTemplate>> GetRankedStrategiesAsync(
        string objectiveType, int limit = 10,
        CancellationToken ct = default);

    // ── Compare ──────────────────────────────────────────────────────

    global::System.Threading.Tasks.Task<StrategyComparisonResult> CompareStrategiesAsync(
        IReadOnlyList<Guid> strategyIds, CancellationToken ct = default);
}
