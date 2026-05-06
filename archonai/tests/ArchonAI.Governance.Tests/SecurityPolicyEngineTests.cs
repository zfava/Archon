using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.AuditLog;
using ArchonAI.Core.Models.Governance;
using ArchonAI.Governance;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Governance.Tests;

public class SecurityPolicyEngineTests
{
    private readonly Mock<IAuditLogService> _auditLogService = new();
    private readonly GovernanceOptions _options = new();

    private SecurityPolicyEngine CreateEngine(GovernanceOptions? options = null)
    {
        SetupDefaultAudit();
        return new SecurityPolicyEngine(Options.Create(options ?? _options), _auditLogService.Object);
    }

    private void SetupDefaultAudit()
    {
        _auditLogService.Setup(a => a.RecordAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditEntry(Guid.NewGuid(), "", "", "", "", "", "", "", "", "",
                new Dictionary<string, string>(), "", null, DateTimeOffset.UtcNow));
    }

    private static Agent CreateAgent(params string[] capabilities)
    {
        var caps = capabilities.Length > 0
            ? capabilities.Select(c => new AgentCapability(c, "", "general", "1.0")).ToArray()
            : new[] { new AgentCapability("data-analysis", "", "analytics", "1.0") };
        return new Agent(Guid.NewGuid(), "test-agent", "1.0", caps, true, DateTimeOffset.UtcNow);
    }

    private static CoreTask CreateTask(string capability = "data-analysis") =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, "test-task", "desc",
            capability, new Dictionary<string, string>(), DateTimeOffset.UtcNow, null, null);

    private static CoreExecutionContext CreateContext(string? permissions = "execute:tasks")
    {
        var metadata = new Dictionary<string, string>();
        if (permissions is not null)
            metadata["permissions"] = permissions;
        return new CoreExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "tenant-1", metadata, DateTimeOffset.UtcNow);
    }

    // ═══════════════════════════════════════════════════════════════
    // EvaluateAgentPermissionsAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EvaluateAgentPermissions_SafeAgent_ReturnsAllowed()
    {
        var engine = CreateEngine();
        var agent = CreateAgent("data-analysis");
        var context = CreateContext("execute:tasks");

        var result = await engine.EvaluateAgentPermissionsAsync(agent, CreateTask(), context);

        result.IsAllowed.Should().BeTrue();
        result.Violations.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateAgentPermissions_DangerousCapability_ReturnsDenied()
    {
        var engine = CreateEngine();
        var agent = CreateAgent("system-admin");

        var result = await engine.EvaluateAgentPermissionsAsync(agent, CreateTask(), CreateContext());

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("deny-dangerous-capabilities"));
    }

    [Fact]
    public async Task EvaluateAgentPermissions_MissingExecutePermission_ReturnsDenied()
    {
        var engine = CreateEngine();
        var agent = CreateAgent("data-analysis");
        var context = CreateContext(permissions: null);

        var result = await engine.EvaluateAgentPermissionsAsync(agent, CreateTask(), context);

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("require-execute-permission"));
    }

    [Fact]
    public async Task EvaluateAgentPermissions_CancellationRequested_Throws()
    {
        var engine = CreateEngine();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.EvaluateAgentPermissionsAsync(CreateAgent(), CreateTask(), CreateContext(), cts.Token));
    }

    [Fact]
    public async Task EvaluateAgentPermissions_IncrementsEvaluationCount()
    {
        var engine = CreateEngine();

        await engine.EvaluateAgentPermissionsAsync(CreateAgent(), CreateTask(), CreateContext());
        await engine.EvaluateAgentPermissionsAsync(CreateAgent(), CreateTask(), CreateContext());

        var metrics = engine.GetMetrics();
        metrics.TotalEvaluations.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task EvaluateAgentPermissions_ViolationIncrementsMetrics()
    {
        var engine = CreateEngine();
        var agent = CreateAgent("system-admin");

        await engine.EvaluateAgentPermissionsAsync(agent, CreateTask(), CreateContext());

        var metrics = engine.GetMetrics();
        metrics.AgentPermissionDenials.Should().BeGreaterThanOrEqualTo(1);
        metrics.TotalViolations.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task EvaluateAgentPermissions_AuditsViolation()
    {
        var engine = CreateEngine();
        var agent = CreateAgent("system-admin");

        await engine.EvaluateAgentPermissionsAsync(agent, CreateTask(), CreateContext());

        _auditLogService.Verify(a => a.RecordAsync(
            "agent-permission-denied", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task EvaluateAgentPermissions_CustomCapabilityAllowlist_EnforcesCorrectly()
    {
        var engine = CreateEngine();
        var allowlistPolicy = new SecurityPolicy(
            Guid.NewGuid(), "custom-allowlist", "agent-permissions",
            new SecurityPolicyRule("capability-allowlist",
                new[] { "approved-cap" }, Array.Empty<string>(), new Dictionary<string, string>()),
            true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await engine.AddPolicyAsync(allowlistPolicy);
        var agent = CreateAgent("unapproved-cap");

        var result = await engine.EvaluateAgentPermissionsAsync(agent, CreateTask(), CreateContext());

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("custom-allowlist"));
    }

    // ═══════════════════════════════════════════════════════════════
    // EvaluateDataAccessAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EvaluateDataAccess_ReadOnNonSensitive_ReturnsAllowed()
    {
        var engine = CreateEngine();

        var result = await engine.EvaluateDataAccessAsync("user-1", "reports", "read");

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateDataAccess_SensitiveResource_ReturnsDenied()
    {
        var engine = CreateEngine();

        var result = await engine.EvaluateDataAccessAsync("user-1", "credentials", "read");

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("credentials"));
    }

    [Fact]
    public async Task EvaluateDataAccess_WriteAction_ReturnsDenied()
    {
        var engine = CreateEngine();

        var result = await engine.EvaluateDataAccessAsync("user-1", "reports", "write");

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("read-only-data-default"));
    }

    // ═══════════════════════════════════════════════════════════════
    // EvaluateWorkflowLimitsAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EvaluateWorkflowLimits_WithinLimits_ReturnsAllowed()
    {
        var engine = CreateEngine();

        var result = await engine.EvaluateWorkflowLimitsAsync(Guid.NewGuid(), 10, 5);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateWorkflowLimits_StepsExceeded_ReturnsDenied()
    {
        var engine = CreateEngine();

        var result = await engine.EvaluateWorkflowLimitsAsync(Guid.NewGuid(), 100, 5);

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("workflow-resource-limits"));
    }

    [Fact]
    public async Task EvaluateWorkflowLimits_AgentsExceeded_ReturnsDenied()
    {
        var engine = CreateEngine();

        var result = await engine.EvaluateWorkflowLimitsAsync(Guid.NewGuid(), 10, 50);

        result.IsAllowed.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════
    // AddPolicyAsync / RemovePolicyAsync / GetPoliciesAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AddPolicy_ThenGetPolicies_ReturnsAdded()
    {
        var engine = CreateEngine();
        var policy = new SecurityPolicy(Guid.NewGuid(), "test-policy", "test-category",
            new SecurityPolicyRule("custom", Array.Empty<string>(), Array.Empty<string>(), new Dictionary<string, string>()),
            true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        await engine.AddPolicyAsync(policy);
        var policies = await engine.GetPoliciesAsync("test-category");

        policies.Should().Contain(p => p.Name == "test-policy");
    }

    [Fact]
    public async Task RemovePolicy_ThenGetPolicies_DoesNotReturnRemoved()
    {
        var engine = CreateEngine();
        var policyId = Guid.NewGuid();
        var policy = new SecurityPolicy(policyId, "removable", "test-category",
            new SecurityPolicyRule("custom", Array.Empty<string>(), Array.Empty<string>(), new Dictionary<string, string>()),
            true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await engine.AddPolicyAsync(policy);

        await engine.RemovePolicyAsync(policyId);
        var policies = await engine.GetPoliciesAsync("test-category");

        policies.Should().NotContain(p => p.Id == policyId);
    }

    [Fact]
    public async Task GetPolicies_NoFilter_ReturnsAllDefaults()
    {
        var engine = CreateEngine();

        var policies = await engine.GetPoliciesAsync();

        policies.Count.Should().BeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public async Task GetPolicies_FilterByCategory_ReturnsOnlyMatching()
    {
        var engine = CreateEngine();

        var agentPolicies = await engine.GetPoliciesAsync("agent-permissions");
        var dataPolicies = await engine.GetPoliciesAsync("data-access");

        agentPolicies.Should().OnlyContain(p => p.Category == "agent-permissions");
        dataPolicies.Should().OnlyContain(p => p.Category == "data-access");
    }

    // ═══════════════════════════════════════════════════════════════
    // GetMetrics
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GetMetrics_InitialState_ReturnsZeroCounts()
    {
        var engine = CreateEngine();

        var metrics = engine.GetMetrics();

        metrics.TotalEvaluations.Should().Be(0);
        metrics.TotalViolations.Should().Be(0);
        metrics.ActivePolicies.Should().BeGreaterThanOrEqualTo(5);
    }
}
