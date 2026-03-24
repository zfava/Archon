using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Sandbox;
using Microsoft.Extensions.Options;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Sandbox;

public sealed class AgentSandboxManager : IAgentSandboxManager
{
    private readonly SandboxOptions _options;
    private readonly ConcurrentDictionary<Guid, int> _activeSandboxExecutions = new();

    public AgentSandboxManager(IOptions<SandboxOptions> options)
    {
        _options = options.Value;
    }

    public global::System.Threading.Tasks.Task<SandboxDecision> EnsureSandboxAsync(
        Agent agent,
        CoreTask task,
        CoreExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var violations = new List<string>();

        int memoryLimitMb = TryReadInt(context.Metadata, "sandbox.memoryLimitMb") ?? _options.DefaultMemoryLimitMb;
        int cpuQuotaPercent = TryReadInt(context.Metadata, "sandbox.cpuQuotaPercent") ?? _options.DefaultCpuQuotaPercent;
        bool networkAllowed = TryReadBool(context.Metadata, "sandbox.networkAccessAllowed") ?? _options.DefaultNetworkAccessAllowed;

        if (memoryLimitMb <= 0)
        {
            violations.Add("memory-limit-invalid");
        }

        if (cpuQuotaPercent <= 0 || cpuQuotaPercent > 100)
        {
            violations.Add("cpu-quota-invalid");
        }

        if (task.RequiredCapability.Contains("external", StringComparison.OrdinalIgnoreCase) && !networkAllowed)
        {
            violations.Add("network-access-restricted");
        }

        IReadOnlyList<string> allowedPermissions = _options.AllowedApiPermissions;
        if (context.Metadata.TryGetValue("permissions", out string? requestedPermissions)
            && !string.IsNullOrWhiteSpace(requestedPermissions))
        {
            var requestSet = requestedPermissions
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (requestSet.Any(permission => !allowedPermissions.Contains(permission, StringComparer.OrdinalIgnoreCase)))
            {
                violations.Add("api-permission-restricted");
            }
        }

        bool allowed = violations.Count == 0;
        Guid sandboxId = Guid.NewGuid();

        if (allowed)
        {
            _activeSandboxExecutions.AddOrUpdate(sandboxId, 1, (_, current) => current + 1);
        }

        return global::System.Threading.Tasks.Task.FromResult(new SandboxDecision(
            IsAllowed: allowed,
            SandboxId: sandboxId,
            Reason: allowed ? "Sandbox constraints satisfied." : $"Sandbox denied execution: {string.Join(',', violations)}",
            Violations: violations,
            MemoryLimitMb: memoryLimitMb,
            CpuQuotaPercent: cpuQuotaPercent,
            NetworkAccessAllowed: networkAllowed,
            AllowedApiPermissions: allowedPermissions));
    }

    public global::System.Threading.Tasks.Task RecordExecutionCompletedAsync(
        Guid sandboxId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _activeSandboxExecutions.TryRemove(sandboxId, out _);
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    private static int? TryReadInt(IReadOnlyDictionary<string, string> metadata, string key)
        => metadata.TryGetValue(key, out string? value) && int.TryParse(value, out int parsed) ? parsed : null;

    private static bool? TryReadBool(IReadOnlyDictionary<string, string> metadata, string key)
        => metadata.TryGetValue(key, out string? value) && bool.TryParse(value, out bool parsed) ? parsed : null;
}
