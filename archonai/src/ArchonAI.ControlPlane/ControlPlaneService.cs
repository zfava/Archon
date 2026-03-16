using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.ControlPlane;
using ArchonAI.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.ControlPlane;

public sealed class ControlPlaneService : IControlPlaneService
{
    private readonly IControlPlaneRepository _repository;
    private readonly IAgentCapabilityRegistry _agentRegistry;
    private readonly IRbacService _rbacService;
    private readonly IEventBus _eventBus;
    private readonly IMonitoringDashboardService _monitoring;
    private readonly ILogger<ControlPlaneService> _logger;
    private readonly ControlPlaneOptions _options;

    private long _tenantOps;
    private long _workflowOps;
    private long _agentOps;
    private long _policyOps;
    private long _configOps;

    public ControlPlaneService(
        IControlPlaneRepository repository,
        IAgentCapabilityRegistry agentRegistry,
        IRbacService rbacService,
        IEventBus eventBus,
        IMonitoringDashboardService monitoring,
        IOptions<ControlPlaneOptions> options,
        ILogger<ControlPlaneService> logger)
    {
        _repository = repository;
        _agentRegistry = agentRegistry;
        _rbacService = rbacService;
        _eventBus = eventBus;
        _monitoring = monitoring;
        _logger = logger;
        _options = options.Value;
    }

    // ══════════════════════════════════════════════════════════
    //  Tenant lifecycle
    // ══════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<Tenant> ProvisionTenantAsync(
        string name, string displayName, TenantTier tier,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("ControlPlane.ProvisionTenant");
        Interlocked.Increment(ref _tenantOps);
        Telemetry.ControlPlaneTenantOps.Add(1);

        // Validate uniqueness
        var existing = await _repository.GetTenantByNameAsync(name, ct);
        if (existing is not null)
            throw new InvalidOperationException($"Tenant with name '{name}' already exists");

        // Enforce tenant limit
        int currentCount = await _repository.CountTenantsAsync(ct: ct);
        if (currentCount >= _options.MaxTenants)
            throw new InvalidOperationException(
                $"Maximum tenant limit ({_options.MaxTenants}) reached");

        var quota = GetDefaultQuota(tier);
        var tenant = new Tenant(
            Id: Guid.NewGuid(),
            Name: name,
            DisplayName: displayName,
            Status: TenantStatus.Provisioning,
            Tier: tier,
            ResourceQuota: quota,
            Metadata: metadata ?? new Dictionary<string, string>(),
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ActivatedAtUtc: null,
            SuspendedAtUtc: null);

        await _repository.UpsertTenantAsync(tenant, ct);

        _logger.LogInformation(
            "Tenant provisioned: {TenantId} '{Name}' tier={Tier}",
            tenant.Id, name, tier);

        await EmitEventAsync("controlplane.tenant.provisioned", tenant.Id.ToString(),
            $"Tenant '{name}' provisioned with tier {tier}");

        return tenant;
    }

    public async global::System.Threading.Tasks.Task<Tenant?> GetTenantAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        return await _repository.GetTenantAsync(tenantId, ct);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<Tenant>> ListTenantsAsync(
        TenantStatus? status = null, int offset = 0, int limit = 50,
        CancellationToken ct = default)
    {
        return await _repository.ListTenantsAsync(status, offset, limit, ct);
    }

    public async global::System.Threading.Tasks.Task<Tenant> ActivateTenantAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("ControlPlane.ActivateTenant");
        Interlocked.Increment(ref _tenantOps);
        Telemetry.ControlPlaneTenantOps.Add(1);

        var tenant = await _repository.GetTenantAsync(tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found");

        if (tenant.Status != TenantStatus.Provisioning && tenant.Status != TenantStatus.Suspended)
            throw new InvalidOperationException(
                $"Tenant {tenantId} cannot be activated from status {tenant.Status}");

        var activated = tenant with
        {
            Status = TenantStatus.Active,
            ActivatedAtUtc = DateTimeOffset.UtcNow,
            SuspendedAtUtc = null
        };
        await _repository.UpsertTenantAsync(activated, ct);

        _logger.LogInformation("Tenant activated: {TenantId} '{Name}'", tenantId, tenant.Name);
        await EmitEventAsync("controlplane.tenant.activated", tenantId.ToString(), tenant.Name);

        return activated;
    }

    public async global::System.Threading.Tasks.Task<Tenant> SuspendTenantAsync(
        Guid tenantId, string reason, CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("ControlPlane.SuspendTenant");
        Interlocked.Increment(ref _tenantOps);
        Telemetry.ControlPlaneTenantOps.Add(1);

        var tenant = await _repository.GetTenantAsync(tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found");

        if (tenant.Status != TenantStatus.Active)
            throw new InvalidOperationException(
                $"Tenant {tenantId} cannot be suspended from status {tenant.Status}");

        var suspended = tenant with
        {
            Status = TenantStatus.Suspended,
            SuspendedAtUtc = DateTimeOffset.UtcNow
        };
        await _repository.UpsertTenantAsync(suspended, ct);

        _logger.LogWarning("Tenant suspended: {TenantId} '{Name}', reason={Reason}",
            tenantId, tenant.Name, reason);
        await EmitEventAsync("controlplane.tenant.suspended", tenantId.ToString(),
            $"Reason: {reason}");

        return suspended;
    }

    public async global::System.Threading.Tasks.Task DeprovisionTenantAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("ControlPlane.DeprovisionTenant");
        Interlocked.Increment(ref _tenantOps);
        Telemetry.ControlPlaneTenantOps.Add(1);

        var tenant = await _repository.GetTenantAsync(tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found");

        if (tenant.Status == TenantStatus.Deprovisioned)
            throw new InvalidOperationException($"Tenant {tenantId} is already deprovisioned");

        // Mark as deprovisioning first
        var deprovisioning = tenant with { Status = TenantStatus.Deprovisioning };
        await _repository.UpsertTenantAsync(deprovisioning, ct);

        // Clean up tenant resources
        var workflows = await _repository.ListWorkflowsAsync(tenantId.ToString(), null, 0, 10000, ct);
        foreach (var wf in workflows)
            await _repository.UpsertWorkflowAsync(wf with { Status = ManagedWorkflowStatus.Archived }, ct);

        var agents = await _repository.ListAgentsAsync(tenantId.ToString(), null, 0, 10000, ct);
        foreach (var agent in agents)
            await _repository.UpsertAgentAsync(agent with { Status = ManagedAgentStatus.Deregistered }, ct);

        // Finalize
        var deprovisioned = tenant with { Status = TenantStatus.Deprovisioned };
        await _repository.UpsertTenantAsync(deprovisioned, ct);

        _logger.LogWarning("Tenant deprovisioned: {TenantId} '{Name}', archived {WfCount} workflows, deregistered {AgentCount} agents",
            tenantId, tenant.Name, workflows.Count, agents.Count);
        await EmitEventAsync("controlplane.tenant.deprovisioned", tenantId.ToString(), tenant.Name);
    }

    // ══════════════════════════════════════════════════════════
    //  Workflow lifecycle
    // ══════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<ManagedWorkflow> RegisterWorkflowAsync(
        string tenantId, string name, string description, string strategy, int stepCount,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("ControlPlane.RegisterWorkflow");
        Interlocked.Increment(ref _workflowOps);
        Telemetry.ControlPlaneWorkflowOps.Add(1);

        await EnforceTenantActiveAsync(tenantId, ct);

        // Enforce workflow quota
        int currentCount = await _repository.CountWorkflowsAsync(tenantId, ct);
        var tenantQuota = await GetTenantQuotaAsync(tenantId, ct);
        if (tenantQuota is not null && currentCount >= tenantQuota.MaxWorkflows)
            throw new InvalidOperationException(
                $"Tenant '{tenantId}' has reached its workflow limit ({tenantQuota.MaxWorkflows})");

        var workflow = new ManagedWorkflow(
            Id: Guid.NewGuid(),
            TenantId: tenantId,
            Name: name,
            Description: description,
            Status: ManagedWorkflowStatus.Draft,
            Strategy: strategy,
            StepCount: stepCount,
            Metadata: metadata ?? new Dictionary<string, string>(),
            CreatedAtUtc: DateTimeOffset.UtcNow,
            LastExecutedAtUtc: null,
            ExecutionCount: 0,
            FailureCount: 0);

        await _repository.UpsertWorkflowAsync(workflow, ct);

        _logger.LogInformation(
            "Workflow registered: {WorkflowId} '{Name}' for tenant '{TenantId}'",
            workflow.Id, name, tenantId);
        await EmitEventAsync("controlplane.workflow.registered",
            workflow.Id.ToString(), $"Workflow '{name}' for tenant '{tenantId}'");

        return workflow;
    }

    public async global::System.Threading.Tasks.Task<ManagedWorkflow?> GetManagedWorkflowAsync(
        Guid workflowId, CancellationToken ct = default)
    {
        return await _repository.GetWorkflowAsync(workflowId, ct);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<ManagedWorkflow>> ListManagedWorkflowsAsync(
        string? tenantId = null, ManagedWorkflowStatus? status = null,
        int offset = 0, int limit = 50, CancellationToken ct = default)
    {
        return await _repository.ListWorkflowsAsync(tenantId, status, offset, limit, ct);
    }

    public async global::System.Threading.Tasks.Task<ManagedWorkflow> UpdateWorkflowStatusAsync(
        Guid workflowId, ManagedWorkflowStatus status, CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("ControlPlane.UpdateWorkflowStatus");
        Interlocked.Increment(ref _workflowOps);
        Telemetry.ControlPlaneWorkflowOps.Add(1);

        var workflow = await _repository.GetWorkflowAsync(workflowId, ct)
            ?? throw new InvalidOperationException($"Workflow {workflowId} not found");

        var updated = workflow with { Status = status };
        await _repository.UpsertWorkflowAsync(updated, ct);

        _logger.LogInformation("Workflow {WorkflowId} status updated to {Status}",
            workflowId, status);
        await EmitEventAsync("controlplane.workflow.status.changed",
            workflowId.ToString(), $"Status changed to {status}");

        return updated;
    }

    // ══════════════════════════════════════════════════════════
    //  Agent lifecycle
    // ══════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<ManagedAgent> RegisterAgentAsync(
        string tenantId, string name, string version, IReadOnlyList<string> capabilities,
        IReadOnlyDictionary<string, string>? configuration = null,
        CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("ControlPlane.RegisterAgent");
        Interlocked.Increment(ref _agentOps);
        Telemetry.ControlPlaneAgentOps.Add(1);

        await EnforceTenantActiveAsync(tenantId, ct);

        // Enforce agent quota
        int currentCount = await _repository.CountAgentsAsync(tenantId, ct);
        var tenantQuota = await GetTenantQuotaAsync(tenantId, ct);
        if (tenantQuota is not null && currentCount >= tenantQuota.MaxAgents)
            throw new InvalidOperationException(
                $"Tenant '{tenantId}' has reached its agent limit ({tenantQuota.MaxAgents})");

        var agent = new ManagedAgent(
            Id: Guid.NewGuid(),
            TenantId: tenantId,
            Name: name,
            Version: version,
            Status: ManagedAgentStatus.Registering,
            Capabilities: capabilities,
            Configuration: configuration ?? new Dictionary<string, string>(),
            RegisteredAtUtc: DateTimeOffset.UtcNow,
            LastActiveAtUtc: null,
            ExecutionCount: 0,
            FailureCount: 0);

        await _repository.UpsertAgentAsync(agent, ct);

        // Register with the global agent registry
        var coreAgent = new Agent(
            agent.Id, name, version,
            capabilities.Select(c => new AgentCapability(c, c, "general", "1.0")).ToList(),
            true, DateTimeOffset.UtcNow);
        await _agentRegistry.RegisterOrUpdateAgentAsync(
            coreAgent, Array.Empty<string>(), Array.Empty<string>(), ct);

        // Transition to active
        var activated = agent with { Status = ManagedAgentStatus.Active };
        await _repository.UpsertAgentAsync(activated, ct);

        _logger.LogInformation(
            "Agent registered: {AgentId} '{Name}' v{Version} for tenant '{TenantId}' with {CapCount} capabilities",
            agent.Id, name, version, tenantId, capabilities.Count);
        await EmitEventAsync("controlplane.agent.registered",
            agent.Id.ToString(), $"Agent '{name}' registered for tenant '{tenantId}'");

        return activated;
    }

    public async global::System.Threading.Tasks.Task<ManagedAgent?> GetManagedAgentAsync(
        Guid agentId, CancellationToken ct = default)
    {
        return await _repository.GetAgentAsync(agentId, ct);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<ManagedAgent>> ListManagedAgentsAsync(
        string? tenantId = null, ManagedAgentStatus? status = null,
        int offset = 0, int limit = 50, CancellationToken ct = default)
    {
        return await _repository.ListAgentsAsync(tenantId, status, offset, limit, ct);
    }

    public async global::System.Threading.Tasks.Task<ManagedAgent> UpdateAgentStatusAsync(
        Guid agentId, ManagedAgentStatus status, CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("ControlPlane.UpdateAgentStatus");
        Interlocked.Increment(ref _agentOps);
        Telemetry.ControlPlaneAgentOps.Add(1);

        var agent = await _repository.GetAgentAsync(agentId, ct)
            ?? throw new InvalidOperationException($"Agent {agentId} not found");

        var updated = agent with { Status = status };
        await _repository.UpsertAgentAsync(updated, ct);

        _logger.LogInformation("Agent {AgentId} status updated to {Status}", agentId, status);
        await EmitEventAsync("controlplane.agent.status.changed",
            agentId.ToString(), $"Status changed to {status}");

        return updated;
    }

    public async global::System.Threading.Tasks.Task DeregisterAgentAsync(
        Guid agentId, CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("ControlPlane.DeregisterAgent");
        Interlocked.Increment(ref _agentOps);
        Telemetry.ControlPlaneAgentOps.Add(1);

        var agent = await _repository.GetAgentAsync(agentId, ct)
            ?? throw new InvalidOperationException($"Agent {agentId} not found");

        // Drain first, then deregister
        var draining = agent with { Status = ManagedAgentStatus.Draining };
        await _repository.UpsertAgentAsync(draining, ct);

        var deregistered = agent with { Status = ManagedAgentStatus.Deregistered };
        await _repository.UpsertAgentAsync(deregistered, ct);

        _logger.LogInformation("Agent deregistered: {AgentId} '{Name}'", agentId, agent.Name);
        await EmitEventAsync("controlplane.agent.deregistered",
            agentId.ToString(), agent.Name);
    }

    // ══════════════════════════════════════════════════════════
    //  Policy management
    // ══════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<PlatformPolicy> CreatePolicyAsync(
        string tenantId, string name, string description, PlatformPolicyType policyType,
        string targetResource, IReadOnlyDictionary<string, string> rules, int priority,
        CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("ControlPlane.CreatePolicy");
        Interlocked.Increment(ref _policyOps);
        Telemetry.ControlPlanePolicyOps.Add(1);

        var policy = new PlatformPolicy(
            Id: Guid.NewGuid(),
            TenantId: tenantId,
            Name: name,
            Description: description,
            PolicyType: policyType,
            TargetResource: targetResource,
            Rules: rules,
            IsEnabled: true,
            Priority: priority,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: null);

        await _repository.UpsertPolicyAsync(policy, ct);

        _logger.LogInformation(
            "Policy created: {PolicyId} '{Name}' type={PolicyType} for tenant '{TenantId}'",
            policy.Id, name, policyType, tenantId);
        await EmitEventAsync("controlplane.policy.created",
            policy.Id.ToString(), $"Policy '{name}' ({policyType})");

        return policy;
    }

    public async global::System.Threading.Tasks.Task<PlatformPolicy?> GetPolicyAsync(
        Guid policyId, CancellationToken ct = default)
    {
        return await _repository.GetPolicyAsync(policyId, ct);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<PlatformPolicy>> ListPoliciesAsync(
        string? tenantId = null, PlatformPolicyType? policyType = null, bool? isEnabled = null,
        int offset = 0, int limit = 50, CancellationToken ct = default)
    {
        return await _repository.ListPoliciesAsync(tenantId, policyType, isEnabled, offset, limit, ct);
    }

    public async global::System.Threading.Tasks.Task<PlatformPolicy> UpdatePolicyAsync(
        Guid policyId, bool isEnabled, IReadOnlyDictionary<string, string>? rules = null,
        int? priority = null, CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("ControlPlane.UpdatePolicy");
        Interlocked.Increment(ref _policyOps);
        Telemetry.ControlPlanePolicyOps.Add(1);

        var policy = await _repository.GetPolicyAsync(policyId, ct)
            ?? throw new InvalidOperationException($"Policy {policyId} not found");

        var updated = policy with
        {
            IsEnabled = isEnabled,
            Rules = rules ?? policy.Rules,
            Priority = priority ?? policy.Priority,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await _repository.UpsertPolicyAsync(updated, ct);

        _logger.LogInformation("Policy {PolicyId} updated: enabled={IsEnabled}", policyId, isEnabled);
        await EmitEventAsync("controlplane.policy.updated", policyId.ToString(), policy.Name);

        return updated;
    }

    public async global::System.Threading.Tasks.Task DeletePolicyAsync(
        Guid policyId, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _policyOps);
        Telemetry.ControlPlanePolicyOps.Add(1);

        var policy = await _repository.GetPolicyAsync(policyId, ct)
            ?? throw new InvalidOperationException($"Policy {policyId} not found");

        await _repository.RemovePolicyAsync(policyId, ct);

        _logger.LogInformation("Policy deleted: {PolicyId} '{Name}'", policyId, policy.Name);
        await EmitEventAsync("controlplane.policy.deleted", policyId.ToString(), policy.Name);
    }

    // ══════════════════════════════════════════════════════════
    //  Configuration management
    // ══════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<PlatformConfiguration> SetConfigurationAsync(
        string tenantId, string scope, string key, string value,
        string? description = null, bool isSecret = false,
        CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("ControlPlane.SetConfiguration");
        Interlocked.Increment(ref _configOps);
        Telemetry.ControlPlaneConfigOps.Add(1);

        var existing = await _repository.GetConfigurationAsync(tenantId, scope, key, ct);

        var config = new PlatformConfiguration(
            Id: existing?.Id ?? Guid.NewGuid(),
            TenantId: tenantId,
            Scope: scope,
            Key: key,
            Value: isSecret ? "***REDACTED***" : value,
            Description: description,
            IsSecret: isSecret,
            CreatedAtUtc: existing?.CreatedAtUtc ?? DateTimeOffset.UtcNow,
            UpdatedAtUtc: existing is not null ? DateTimeOffset.UtcNow : null);

        await _repository.UpsertConfigurationAsync(config with { Value = value }, ct);

        _logger.LogInformation(
            "Configuration set: tenant='{TenantId}' scope='{Scope}' key='{Key}' secret={IsSecret}",
            tenantId, scope, key, isSecret);
        await EmitEventAsync("controlplane.config.set",
            $"{tenantId}:{scope}:{key}", $"Configuration '{key}' set in scope '{scope}'");

        return config;
    }

    public async global::System.Threading.Tasks.Task<PlatformConfiguration?> GetConfigurationAsync(
        string tenantId, string scope, string key, CancellationToken ct = default)
    {
        var config = await _repository.GetConfigurationAsync(tenantId, scope, key, ct);
        if (config is null) return null;

        // Mask secret values on read
        return config.IsSecret ? config with { Value = "***REDACTED***" } : config;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<PlatformConfiguration>> ListConfigurationsAsync(
        string? tenantId = null, string? scope = null,
        int offset = 0, int limit = 100, CancellationToken ct = default)
    {
        var configs = await _repository.ListConfigurationsAsync(tenantId, scope, offset, limit, ct);

        // Mask secrets
        return configs.Select(c => c.IsSecret ? c with { Value = "***REDACTED***" } : c).ToList();
    }

    public async global::System.Threading.Tasks.Task DeleteConfigurationAsync(
        string tenantId, string scope, string key, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _configOps);
        Telemetry.ControlPlaneConfigOps.Add(1);

        bool removed = await _repository.RemoveConfigurationAsync(tenantId, scope, key, ct);
        if (!removed)
            throw new InvalidOperationException(
                $"Configuration '{key}' not found in scope '{scope}' for tenant '{tenantId}'");

        _logger.LogInformation("Configuration deleted: tenant='{TenantId}' scope='{Scope}' key='{Key}'",
            tenantId, scope, key);
    }

    // ══════════════════════════════════════════════════════════
    //  Dashboard & status
    // ══════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<ControlPlaneDashboard> GetDashboardAsync(
        CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("ControlPlane.Dashboard");

        var status = GetStatus();
        var tenants = await _repository.ListTenantsAsync(null, 0, 100, ct);
        var policies = await _repository.ListPoliciesAsync(null, null, true, 0, 100, ct);

        var tenantSummaries = new List<TenantSummary>();
        foreach (var t in tenants)
        {
            int agentCount = await _repository.CountAgentsAsync(t.Id.ToString(), ct);
            int wfCount = await _repository.CountWorkflowsAsync(t.Id.ToString(), ct);
            tenantSummaries.Add(new TenantSummary(
                t.Id, t.Name, t.Status, t.Tier,
                agentCount, wfCount, t.CreatedAtUtc));
        }

        var policySummaries = policies.Select(p => new PolicySummary(
            p.Id, p.Name, p.PolicyType, p.TargetResource,
            p.IsEnabled, p.Priority)).ToList();

        return new ControlPlaneDashboard(
            Status: status,
            Tenants: tenantSummaries,
            ActivePolicies: policySummaries,
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    public ControlPlaneStatus GetStatus()
    {
        int totalTenants = _repository.CountTenantsAsync().GetAwaiter().GetResult();
        int activeTenants = _repository.CountTenantsAsync(TenantStatus.Active).GetAwaiter().GetResult();
        int totalWorkflows = _repository.CountWorkflowsAsync().GetAwaiter().GetResult();
        int totalAgents = _repository.CountAgentsAsync().GetAwaiter().GetResult();
        int totalPolicies = _repository.CountPoliciesAsync().GetAwaiter().GetResult();
        int totalConfigs = _repository.CountConfigurationsAsync().GetAwaiter().GetResult();

        return new ControlPlaneStatus(
            IsActive: true,
            TotalTenants: totalTenants,
            ActiveTenants: activeTenants,
            TotalManagedWorkflows: totalWorkflows,
            TotalManagedAgents: totalAgents,
            TotalPolicies: totalPolicies,
            TotalConfigurations: totalConfigs,
            TenantOperations: Interlocked.Read(ref _tenantOps),
            WorkflowOperations: Interlocked.Read(ref _workflowOps),
            AgentOperations: Interlocked.Read(ref _agentOps),
            PolicyOperations: Interlocked.Read(ref _policyOps),
            ConfigOperations: Interlocked.Read(ref _configOps),
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    // ══════════════════════════════════════════════════════════
    //  Private helpers
    // ══════════════════════════════════════════════════════════

    private static TenantResourceQuota GetDefaultQuota(TenantTier tier) => tier switch
    {
        TenantTier.Free => new TenantResourceQuota(
            MaxAgents: 5, MaxWorkflows: 10, MaxConcurrentExecutions: 2,
            MaxStorageBytes: 100 * 1024 * 1024, MaxEventsPerMinute: 100),
        TenantTier.Standard => new TenantResourceQuota(
            MaxAgents: 25, MaxWorkflows: 100, MaxConcurrentExecutions: 10,
            MaxStorageBytes: 1024L * 1024 * 1024, MaxEventsPerMinute: 1000),
        TenantTier.Professional => new TenantResourceQuota(
            MaxAgents: 100, MaxWorkflows: 500, MaxConcurrentExecutions: 50,
            MaxStorageBytes: 10L * 1024 * 1024 * 1024, MaxEventsPerMinute: 5000),
        TenantTier.Enterprise => new TenantResourceQuota(
            MaxAgents: 1000, MaxWorkflows: 5000, MaxConcurrentExecutions: 500,
            MaxStorageBytes: 100L * 1024 * 1024 * 1024, MaxEventsPerMinute: 50000),
        _ => throw new ArgumentOutOfRangeException(nameof(tier))
    };

    private async global::System.Threading.Tasks.Task EnforceTenantActiveAsync(
        string tenantId, CancellationToken ct)
    {
        var tenants = await _repository.ListTenantsAsync(null, 0, 10000, ct);
        var tenant = tenants.FirstOrDefault(t =>
            t.Id.ToString() == tenantId || t.Name == tenantId);

        if (tenant is null)
            throw new InvalidOperationException($"Tenant '{tenantId}' not found");

        if (tenant.Status != TenantStatus.Active)
            throw new InvalidOperationException(
                $"Tenant '{tenantId}' is not active (current status: {tenant.Status})");
    }

    private async global::System.Threading.Tasks.Task<TenantResourceQuota?> GetTenantQuotaAsync(
        string tenantId, CancellationToken ct)
    {
        var tenants = await _repository.ListTenantsAsync(null, 0, 10000, ct);
        var tenant = tenants.FirstOrDefault(t =>
            t.Id.ToString() == tenantId || t.Name == tenantId);
        return tenant?.ResourceQuota;
    }

    private async global::System.Threading.Tasks.Task EmitEventAsync(
        string eventType, string resourceId, string description)
    {
        try
        {
            await _eventBus.PublishAsync(new SystemEvent(
                Guid.NewGuid(), eventType, "ControlPlaneService",
                Guid.NewGuid(),
                new Dictionary<string, string>
                {
                    ["resourceId"] = resourceId,
                    ["description"] = description
                },
                DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit control plane event {EventType}", eventType);
        }
    }
}
