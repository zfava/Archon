using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ArchonAI.Common.Observability;

/// <summary>
/// Health check that gates readiness based on production configuration validation.
///
/// In production/staging: reports <see cref="HealthStatus.Unhealthy"/> when critical
/// misconfigurations are detected, preventing K8s from routing traffic.
///
/// In development/test: reports <see cref="HealthStatus.Degraded"/> for the same
/// misconfigurations, allowing local development to proceed.
/// </summary>
public sealed class ProductionConfigHealthCheck : IHealthCheck
{
    private static ConfigValidationResult? _result;
    private static readonly object _lock = new();

    /// <summary>
    /// Called once at startup to set the validation result.
    /// </summary>
    public static void SetResult(ConfigValidationResult result)
    {
        lock (_lock)
        {
            _result = result;
        }
    }

    /// <summary>
    /// Returns the current validation result (for testing/inspection).
    /// </summary>
    public static ConfigValidationResult? GetResult()
    {
        lock (_lock)
        {
            return _result;
        }
    }

    /// <summary>
    /// Resets stored state. Intended for test isolation only.
    /// </summary>
    public static void Reset()
    {
        lock (_lock)
        {
            _result = null;
        }
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ConfigValidationResult? result;
        lock (_lock)
        {
            result = _result;
        }

        if (result is null)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Configuration validation has not run yet. Startup may be incomplete."));
        }

        if (result.HasCriticalFindings && result.IsProductionMode)
        {
            var criticals = result.Findings
                .Where(f => f.Severity == ConfigSeverity.Critical)
                .Select(f => $"[{f.Component}] {f.Message}");

            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Production configuration validation failed. " +
                $"Critical issues: {string.Join(" | ", criticals)}",
                data: new Dictionary<string, object>
                {
                    ["environment"] = result.Environment,
                    ["criticalCount"] = result.Findings.Count(f => f.Severity == ConfigSeverity.Critical),
                    ["warningCount"] = result.Findings.Count(f => f.Severity == ConfigSeverity.Warning),
                }));
        }

        if (result.HasCriticalFindings)
        {
            // Dev/test — report degraded but don't block
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Configuration has issues that would be critical in production " +
                $"({result.Findings.Count(f => f.Severity == ConfigSeverity.Critical)} critical findings). " +
                $"Acceptable for {result.Environment} environment."));
        }

        if (result.HasWarnings)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Configuration validation passed with " +
                $"{result.Findings.Count(f => f.Severity == ConfigSeverity.Warning)} warnings."));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "All production configuration checks passed."));
    }
}
