namespace ArchonAI.Core.Models.Sandbox;

public sealed record SandboxDecision(
    bool IsAllowed,
    Guid SandboxId,
    string Reason,
    IReadOnlyList<string> Violations,
    int MemoryLimitMb,
    int CpuQuotaPercent,
    bool NetworkAccessAllowed,
    IReadOnlyList<string> AllowedApiPermissions);
