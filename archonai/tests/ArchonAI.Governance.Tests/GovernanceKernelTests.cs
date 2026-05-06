using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.AuditLog;
using ArchonAI.Core.Models.Governance;
using ArchonAI.Core.Models.Identity;
using ArchonAI.Core.Models.Policy;
using ArchonAI.Governance;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Governance.Tests;

public class GovernanceKernelTests
{
    private readonly Mock<IPolicyEngine> _policyEngine = new();
    private readonly Mock<IAgentIdentityStore> _identityStore = new();
    private readonly Mock<ISecurityPolicyEngine> _securityPolicyEngine = new();
    private readonly Mock<IAuditLogService> _auditLogService = new();
    private readonly GovernanceOptions _options = new();

    private GovernanceKernel CreateKernel(GovernanceOptions? options = null)
    {
        var opts = Options.Create(options ?? _options);
        SetupDefaultAudit();
        return new GovernanceKernel(opts, _policyEngine.Object, _identityStore.Object,
            _securityPolicyEngine.Object, _auditLogService.Object);
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

    private static Agent CreateAgent(
        Guid? id = null,
        string name = "test-agent",
        IReadOnlyList<AgentCapability>? capabilities = null)
    {
        return new Agent(
            id ?? Guid.NewGuid(),
            name,
            "1.0",
            capabilities ?? new[] { new AgentCapability("data-analysis", "Analyze data", "analytics", "1.0") },
            true,
            DateTimeOffset.UtcNow);
    }

    private static CoreTask CreateTask(string capability = "data-analysis")
    {
        return new CoreTask(Guid.NewGuid(), Guid.NewGuid(), 1, "test-task", "desc",
            capability, new Dictionary<string, string>(), DateTimeOffset.UtcNow, null, null);
    }

    private static CoreExecutionContext CreateContext(string? permissions = "execute:tasks")
    {
        var metadata = new Dictionary<string, string>();
        if (permissions is not null)
            metadata["permissions"] = permissions;
        return new CoreExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "tenant-1", metadata, DateTimeOffset.UtcNow);
    }

    private static AgentIdentityProfile CreateIdentity(
        Guid agentId,
        IReadOnlyList<string>? capabilities = null,
        IReadOnlyList<string>? permissions = null,
        int totalExec = 10,
        int successExec = 9)
    {
        return new AgentIdentityProfile(
            agentId,
            capabilities ?? new[] { "data-analysis" },
            permissions ?? new[] { "execute:tasks" },
            new AgentPerformanceMetrics(totalExec, successExec, totalExec - successExec, 100, 1.0m, DateTimeOffset.UtcNow),
            Array.Empty<AgentExecutionHistoryEntry>(),
            DateTimeOffset.UtcNow);
    }

    private static PolicyDecision AllowedPolicy() =>
        new(true, 0, 1, false, "n/a", "none", "none", Array.Empty<string>(), "ok", DateTimeOffset.UtcNow);

    private static PolicyDecision DeniedPolicy() =>
        new(false, 0.9, 0.5, true, "pending", "none", "review", new[] { "guardrail-1" }, "denied", DateTimeOffset.UtcNow);

    private static SecurityEvaluationResult AllowedSecurity() =>
        new(true, Array.Empty<SecurityPolicyEvaluation>(), Array.Empty<string>(), DateTimeOffset.UtcNow);

    private static SecurityEvaluationResult DeniedSecurity(params string[] violations) =>
        new(false, Array.Empty<SecurityPolicyEvaluation>(), violations, DateTimeOffset.UtcNow);

    // ═══════════════════════════════════════════════════════════════
    // ValidateAgentRegistrationAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ValidateAgentRegistration_HappyPath_ReturnsAllowed()
    {
        _identityStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentIdentityProfile?)null);
        var kernel = CreateKernel();
        var agent = CreateAgent();

        var result = await kernel.ValidateAgentRegistrationAsync(agent);

        result.IsAllowed.Should().BeTrue();
        result.Violations.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAgentRegistration_RegistrationDisabled_ReturnsDenied()
    {
        _options.AllowAgentRegistration = false;
        _identityStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentIdentityProfile?)null);
        var kernel = CreateKernel();

        var result = await kernel.ValidateAgentRegistrationAsync(CreateAgent());

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().Contain("registration-disabled");
    }

    [Fact]
    public async Task ValidateAgentRegistration_EmptyName_ReturnsDenied()
    {
        _identityStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentIdentityProfile?)null);
        var kernel = CreateKernel();
        var agent = CreateAgent(name: "  ");

        var result = await kernel.ValidateAgentRegistrationAsync(agent);

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().Contain("agent-name-missing");
    }

    [Fact]
    public async Task ValidateAgentRegistration_NoCapabilities_ReturnsDenied()
    {
        _identityStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentIdentityProfile?)null);
        var kernel = CreateKernel();
        var agent = CreateAgent(capabilities: Array.Empty<AgentCapability>());

        var result = await kernel.ValidateAgentRegistrationAsync(agent);

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().Contain("capability-missing");
    }

    [Fact]
    public async Task ValidateAgentRegistration_DisallowedCapabilities_ReturnsDenied()
    {
        _options.AllowedCapabilities = new List<string> { "approved-cap" };
        _identityStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentIdentityProfile?)null);
        var kernel = CreateKernel();
        var agent = CreateAgent(capabilities: new[] { new AgentCapability("unapproved", "", "", "1.0") });

        var result = await kernel.ValidateAgentRegistrationAsync(agent);

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().Contain("disallowed-capabilities");
    }

    [Fact]
    public async Task ValidateAgentRegistration_IdentityCapabilityMismatch_ReturnsDenied()
    {
        var agentId = Guid.NewGuid();
        var identity = CreateIdentity(agentId, capabilities: new[] { "different-capability" });
        _identityStore.Setup(s => s.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity);
        var kernel = CreateKernel();
        var agent = CreateAgent(id: agentId, capabilities: new[] { new AgentCapability("non-matching", "", "", "1.0") });

        var result = await kernel.ValidateAgentRegistrationAsync(agent);

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().Contain("identity-capability-mismatch");
    }

    [Fact]
    public async Task ValidateAgentRegistration_CancellationRequested_Throws()
    {
        var kernel = CreateKernel();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => kernel.ValidateAgentRegistrationAsync(CreateAgent(), cts.Token));
    }

    [Fact]
    public async Task ValidateAgentRegistration_MultipleViolations_CollectsAll()
    {
        _options.AllowAgentRegistration = false;
        _identityStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentIdentityProfile?)null);
        var kernel = CreateKernel();
        var agent = CreateAgent(name: "", capabilities: Array.Empty<AgentCapability>());

        var result = await kernel.ValidateAgentRegistrationAsync(agent);

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().Contain("registration-disabled");
        result.Violations.Should().Contain("agent-name-missing");
        result.Violations.Should().Contain("capability-missing");
    }

    // ═══════════════════════════════════════════════════════════════
    // ValidateExecutionAsync
    // ═══════════════════════════════════════════════════════════════

    private void SetupHappyPathExecution(Guid agentId)
    {
        _identityStore.Setup(s => s.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIdentity(agentId));
        _securityPolicyEngine.Setup(s => s.EvaluateAgentPermissionsAsync(
            It.IsAny<Agent>(), It.IsAny<CoreTask>(), It.IsAny<CoreExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedSecurity());
        _policyEngine.Setup(p => p.EvaluateAsync(
            It.IsAny<Agent>(), It.IsAny<CoreTask>(), It.IsAny<CoreExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedPolicy());
    }

    [Fact]
    public async Task ValidateExecution_HappyPath_ReturnsAllowed()
    {
        var agentId = Guid.NewGuid();
        SetupHappyPathExecution(agentId);
        var kernel = CreateKernel();
        var agent = CreateAgent(id: agentId);

        var result = await kernel.ValidateExecutionAsync(agent, CreateTask(), CreateContext());

        result.IsAllowed.Should().BeTrue();
        result.Violations.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateExecution_IdentityNotFound_ReturnsDenied()
    {
        _identityStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentIdentityProfile?)null);
        _securityPolicyEngine.Setup(s => s.EvaluateAgentPermissionsAsync(
            It.IsAny<Agent>(), It.IsAny<CoreTask>(), It.IsAny<CoreExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedSecurity());
        _policyEngine.Setup(p => p.EvaluateAsync(
            It.IsAny<Agent>(), It.IsAny<CoreTask>(), It.IsAny<CoreExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedPolicy());
        var kernel = CreateKernel();

        var result = await kernel.ValidateExecutionAsync(CreateAgent(), CreateTask(), CreateContext());

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().Contain("identity-not-found");
    }

    [Fact]
    public async Task ValidateExecution_IdentityCapabilityMismatch_ReturnsDenied()
    {
        var agentId = Guid.NewGuid();
        var identity = CreateIdentity(agentId, capabilities: new[] { "wrong-capability" });
        _identityStore.Setup(s => s.GetAsync(agentId, It.IsAny<CancellationToken>())).ReturnsAsync(identity);
        _securityPolicyEngine.Setup(s => s.EvaluateAgentPermissionsAsync(
            It.IsAny<Agent>(), It.IsAny<CoreTask>(), It.IsAny<CoreExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedSecurity());
        _policyEngine.Setup(p => p.EvaluateAsync(
            It.IsAny<Agent>(), It.IsAny<CoreTask>(), It.IsAny<CoreExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedPolicy());
        var kernel = CreateKernel();

        var result = await kernel.ValidateExecutionAsync(
            CreateAgent(id: agentId), CreateTask(), CreateContext());

        result.Violations.Should().Contain("identity-capability-validation-failed");
    }

    [Fact]
    public async Task ValidateExecution_IdentityPermissionMissing_ReturnsDenied()
    {
        var agentId = Guid.NewGuid();
        var identity = CreateIdentity(agentId, permissions: new[] { "read:only" });
        _identityStore.Setup(s => s.GetAsync(agentId, It.IsAny<CancellationToken>())).ReturnsAsync(identity);
        _securityPolicyEngine.Setup(s => s.EvaluateAgentPermissionsAsync(
            It.IsAny<Agent>(), It.IsAny<CoreTask>(), It.IsAny<CoreExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedSecurity());
        _policyEngine.Setup(p => p.EvaluateAsync(
            It.IsAny<Agent>(), It.IsAny<CoreTask>(), It.IsAny<CoreExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedPolicy());
        var kernel = CreateKernel();

        var result = await kernel.ValidateExecutionAsync(
            CreateAgent(id: agentId), CreateTask(), CreateContext());

        result.Violations.Should().Contain("identity-permission-enforcement-failed");
    }

    [Fact]
    public async Task ValidateExecution_PerformanceBelowThreshold_ReturnsDenied()
    {
        var agentId = Guid.NewGuid();
        var identity = CreateIdentity(agentId, totalExec: 10, successExec: 1);
        _identityStore.Setup(s => s.GetAsync(agentId, It.IsAny<CancellationToken>())).ReturnsAsync(identity);
        _securityPolicyEngine.Setup(s => s.EvaluateAgentPermissionsAsync(
            It.IsAny<Agent>(), It.IsAny<CoreTask>(), It.IsAny<CoreExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedSecurity());
        _policyEngine.Setup(p => p.EvaluateAsync(
            It.IsAny<Agent>(), It.IsAny<CoreTask>(), It.IsAny<CoreExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedPolicy());
        var kernel = CreateKernel();

        var result = await kernel.ValidateExecutionAsync(
            CreateAgent(id: agentId), CreateTask(), CreateContext());

        result.Violations.Should().Contain("identity-performance-below-threshold");
    }

    [Fact]
    public async Task ValidateExecution_AgentCapabilityMismatch_ReturnsDenied()
    {
        var agentId = Guid.NewGuid();
        SetupHappyPathExecution(agentId);
        var kernel = CreateKernel();
        var agent = CreateAgent(id: agentId, capabilities: new[] { new AgentCapability("other", "", "", "1.0") });

        var result = await kernel.ValidateExecutionAsync(agent, CreateTask("data-analysis"), CreateContext());

        result.Violations.Should().Contain("capability-validation-failed");
    }

    [Fact]
    public async Task ValidateExecution_ContextPermissionMissing_ReturnsDenied()
    {
        var agentId = Guid.NewGuid();
        SetupHappyPathExecution(agentId);
        var kernel = CreateKernel();
        var context = CreateContext(permissions: null);

        var result = await kernel.ValidateExecutionAsync(CreateAgent(id: agentId), CreateTask(), context);

        result.Violations.Should().Contain("permission-enforcement-failed");
    }

    [Fact]
    public async Task ValidateExecution_PermissionCheckDisabled_SkipsPermissionValidation()
    {
        _options.EnforcePermissionCheck = false;
        var agentId = Guid.NewGuid();
        var identity = CreateIdentity(agentId, permissions: Array.Empty<string>());
        _identityStore.Setup(s => s.GetAsync(agentId, It.IsAny<CancellationToken>())).ReturnsAsync(identity);
        _securityPolicyEngine.Setup(s => s.EvaluateAgentPermissionsAsync(
            It.IsAny<Agent>(), It.IsAny<CoreTask>(), It.IsAny<CoreExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedSecurity());
        _policyEngine.Setup(p => p.EvaluateAsync(
            It.IsAny<Agent>(), It.IsAny<CoreTask>(), It.IsAny<CoreExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedPolicy());
        var kernel = CreateKernel();
        var context = CreateContext(permissions: null);

        var result = await kernel.ValidateExecutionAsync(CreateAgent(id: agentId), CreateTask(), context);

        result.Violations.Should().NotContain("permission-enforcement-failed");
        result.Violations.Should().NotContain("identity-permission-enforcement-failed");
    }

    [Fact]
    public async Task ValidateExecution_ConcurrencyExceeded_ReturnsDenied()
    {
        _options.MaxConcurrentExecutionsPerAgent = 1;
        var agentId = Guid.NewGuid();
        SetupHappyPathExecution(agentId);
        var kernel = CreateKernel();
        var agent = CreateAgent(id: agentId);

        await kernel.ValidateExecutionAsync(agent, CreateTask(), CreateContext());
        var result = await kernel.ValidateExecutionAsync(agent, CreateTask(), CreateContext());

        result.Violations.Should().Contain("resource-quota-concurrent-exceeded");
    }

    [Fact]
    public async Task ValidateExecution_HourlyRateExceeded_ReturnsDenied()
    {
        _options.MaxExecutionsPerHourPerAgent = 1;
        var agentId = Guid.NewGuid();
        SetupHappyPathExecution(agentId);
        var kernel = CreateKernel();
        var agent = CreateAgent(id: agentId);

        await kernel.ValidateExecutionAsync(agent, CreateTask(), CreateContext());
        await kernel.MarkExecutionCompletedAsync(agentId);
        var result = await kernel.ValidateExecutionAsync(agent, CreateTask(), CreateContext());

        result.Violations.Should().Contain("resource-quota-hourly-exceeded");
    }

    [Fact]
    public async Task ValidateExecution_SecurityViolation_PropagatesViolations()
    {
        var agentId = Guid.NewGuid();
        _identityStore.Setup(s => s.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIdentity(agentId));
        _securityPolicyEngine.Setup(s => s.EvaluateAgentPermissionsAsync(
            It.IsAny<Agent>(), It.IsAny<CoreTask>(), It.IsAny<CoreExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DeniedSecurity("agent-permission:deny-dangerous-capabilities"));
        _policyEngine.Setup(p => p.EvaluateAsync(
            It.IsAny<Agent>(), It.IsAny<CoreTask>(), It.IsAny<CoreExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedPolicy());
        var kernel = CreateKernel();

        var result = await kernel.ValidateExecutionAsync(CreateAgent(id: agentId), CreateTask(), CreateContext());

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().Contain(v => v.StartsWith("security:"));
    }

    [Fact]
    public async Task ValidateExecution_PolicyDenied_ReturnsDenied()
    {
        var agentId = Guid.NewGuid();
        _identityStore.Setup(s => s.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIdentity(agentId));
        _securityPolicyEngine.Setup(s => s.EvaluateAgentPermissionsAsync(
            It.IsAny<Agent>(), It.IsAny<CoreTask>(), It.IsAny<CoreExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedSecurity());
        _policyEngine.Setup(p => p.EvaluateAsync(
            It.IsAny<Agent>(), It.IsAny<CoreTask>(), It.IsAny<CoreExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DeniedPolicy());
        var kernel = CreateKernel();

        var result = await kernel.ValidateExecutionAsync(CreateAgent(id: agentId), CreateTask(), CreateContext());

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().Contain("policy-check-failed");
    }

    [Fact]
    public async Task ValidateExecution_Allowed_IncrementsActiveCount()
    {
        _options.MaxConcurrentExecutionsPerAgent = 2;
        var agentId = Guid.NewGuid();
        SetupHappyPathExecution(agentId);
        var kernel = CreateKernel();
        var agent = CreateAgent(id: agentId);

        var first = await kernel.ValidateExecutionAsync(agent, CreateTask(), CreateContext());
        var second = await kernel.ValidateExecutionAsync(agent, CreateTask(), CreateContext());
        var third = await kernel.ValidateExecutionAsync(agent, CreateTask(), CreateContext());

        first.IsAllowed.Should().BeTrue();
        second.IsAllowed.Should().BeTrue();
        third.Violations.Should().Contain("resource-quota-concurrent-exceeded");
    }

    [Fact]
    public async Task ValidateExecution_CancellationRequested_Throws()
    {
        var kernel = CreateKernel();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => kernel.ValidateExecutionAsync(CreateAgent(), CreateTask(), CreateContext(), cts.Token));
    }

    // ═══════════════════════════════════════════════════════════════
    // MarkExecutionCompletedAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarkExecutionCompleted_DecrementsActiveCount()
    {
        _options.MaxConcurrentExecutionsPerAgent = 1;
        var agentId = Guid.NewGuid();
        SetupHappyPathExecution(agentId);
        var kernel = CreateKernel();
        var agent = CreateAgent(id: agentId);

        await kernel.ValidateExecutionAsync(agent, CreateTask(), CreateContext());
        await kernel.MarkExecutionCompletedAsync(agentId);
        var result = await kernel.ValidateExecutionAsync(agent, CreateTask(), CreateContext());

        result.Violations.Should().NotContain("resource-quota-concurrent-exceeded");
    }

    [Fact]
    public async Task MarkExecutionCompleted_NeverGoesNegative()
    {
        var kernel = CreateKernel();
        var agentId = Guid.NewGuid();

        await kernel.MarkExecutionCompletedAsync(agentId);
        await kernel.MarkExecutionCompletedAsync(agentId);

        SetupHappyPathExecution(agentId);
        var result = await kernel.ValidateExecutionAsync(CreateAgent(id: agentId), CreateTask(), CreateContext());
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task MarkExecutionCompleted_CancellationRequested_Throws()
    {
        var kernel = CreateKernel();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => kernel.MarkExecutionCompletedAsync(Guid.NewGuid(), cts.Token));
    }

    [Fact]
    public async Task MarkExecutionCompleted_AuditsCalled()
    {
        var kernel = CreateKernel();
        var agentId = Guid.NewGuid();

        await kernel.MarkExecutionCompletedAsync(agentId);

        _auditLogService.Verify(a => a.RecordAsync(
            "execution-completed", It.IsAny<string>(), It.IsAny<string>(),
            agentId.ToString(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
