using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ArchonAI.Common.Observability;

/// <summary>
/// Health check that reports the full fallback posture of all enterprise-critical subsystems.
/// Each subsystem registers its resolved backing implementation at startup, and this check
/// aggregates the information into a single health report.
///
/// In production-like environments, any in-memory backing for a critical subsystem
/// causes the check to report <see cref="HealthStatus.Unhealthy"/>.
/// In dev/test, the same condition reports <see cref="HealthStatus.Degraded"/>.
/// </summary>
public sealed class FallbackPostureHealthCheck : IHealthCheck
{
    private static readonly object _lock = new();
    private static readonly Dictionary<string, SubsystemPosture> _postures = new();
    private static bool _isProductionLike;

    /// <summary>
    /// Records the resolved backing type for a subsystem.
    /// Called once per subsystem during service resolution.
    /// </summary>
    public static void RecordPosture(string subsystem, string backingType, bool isDurable)
    {
        lock (_lock)
        {
            _postures[subsystem] = new SubsystemPosture(backingType, isDurable);
        }
    }

    /// <summary>
    /// Sets the environment context.
    /// </summary>
    public static void SetEnvironment(bool isProductionLike)
    {
        lock (_lock)
        {
            _isProductionLike = isProductionLike;
        }
    }

    /// <summary>
    /// Returns a snapshot of all recorded postures (for testing/inspection).
    /// </summary>
    public static IReadOnlyDictionary<string, SubsystemPosture> GetPostures()
    {
        lock (_lock)
        {
            return new Dictionary<string, SubsystemPosture>(_postures);
        }
    }

    /// <summary>
    /// Resets stored state. Intended for test isolation only.
    /// </summary>
    public static void Reset()
    {
        lock (_lock)
        {
            _postures.Clear();
            _isProductionLike = false;
        }
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        Dictionary<string, SubsystemPosture> snapshot;
        bool isProd;
        lock (_lock)
        {
            snapshot = new Dictionary<string, SubsystemPosture>(_postures);
            isProd = _isProductionLike;
        }

        if (snapshot.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "No subsystem postures recorded yet."));
        }

        var inMemorySubsystems = snapshot
            .Where(kv => !kv.Value.IsDurable)
            .Select(kv => $"{kv.Key}={kv.Value.BackingType}")
            .ToList();

        var data = new Dictionary<string, object>
        {
            ["environment"] = isProd ? "production-like" : "development",
            ["totalSubsystems"] = snapshot.Count,
            ["durableCount"] = snapshot.Count(kv => kv.Value.IsDurable),
            ["inMemoryCount"] = inMemorySubsystems.Count,
        };

        foreach (var (name, posture) in snapshot)
        {
            data[$"subsystem:{name}"] = posture.IsDurable ? $"DURABLE ({posture.BackingType})" : $"IN-MEMORY ({posture.BackingType})";
        }

        if (inMemorySubsystems.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                $"All {snapshot.Count} enterprise-critical subsystems are using durable persistence.",
                data: data));
        }

        var summary = $"{inMemorySubsystems.Count} subsystem(s) using in-memory fallback: {string.Join(", ", inMemorySubsystems)}";

        if (isProd)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"PRODUCTION: {summary}. In-memory fallbacks are not permitted in production-like environments.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Degraded(
            $"DEV: {summary}. Acceptable for local development only.",
            data: data));
    }
}

/// <summary>
/// Records the backing implementation type and durability of a subsystem.
/// </summary>
public sealed record SubsystemPosture(string BackingType, bool IsDurable);
