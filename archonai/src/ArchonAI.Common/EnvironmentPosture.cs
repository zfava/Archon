using Microsoft.Extensions.Logging;

namespace ArchonAI.Common;

/// <summary>
/// Shared environment posture signal used by DI factories to determine
/// whether in-memory fallbacks are acceptable or must be hard-blocked.
///
/// Set once at startup from IHostEnvironment. Factories that resolve
/// enterprise-critical stores check <see cref="RequireDurablePersistence"/>
/// to decide whether a missing connection string is a fatal configuration error.
/// </summary>
public sealed class EnvironmentPosture
{
    /// <summary>
    /// True when the application is running in a production-like environment
    /// (Production, Staging, or any non-Development/Testing mode).
    /// </summary>
    public bool IsProductionLike { get; init; }

    /// <summary>
    /// Convenience: true when in-memory fallbacks must NOT be silently used
    /// for enterprise-critical stores (identity, governance, audit, control plane, etc.).
    /// </summary>
    public bool RequireDurablePersistence => IsProductionLike;

    /// <summary>
    /// Guards a factory fallback. In production-like environments, throws
    /// <see cref="InvalidOperationException"/> with actionable guidance.
    /// In dev/test, logs a warning and allows the in-memory fallback.
    /// </summary>
    public void GuardInMemoryFallback(
        string subsystem,
        string configKey,
        ILogger logger)
    {
        if (IsProductionLike)
        {
            throw new InvalidOperationException(
                $"[{subsystem}] In-memory fallback is not permitted in production-like environments. " +
                $"Configure '{configKey}' with a durable backing store (PostgreSQL / NATS). " +
                $"Current environment requires durable persistence for all enterprise-critical stores.");
        }

        logger.LogWarning(
            "[{Subsystem}] Using in-memory implementation. This is acceptable for local " +
            "development but NOT safe for production or multi-instance deployment. " +
            "Set '{ConfigKey}' for durable persistence.",
            subsystem, configKey);
    }
}
