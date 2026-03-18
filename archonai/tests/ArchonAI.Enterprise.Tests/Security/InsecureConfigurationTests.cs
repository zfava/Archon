using ArchonAI.Policy;
using ArchonAI.MultiTenant;
using Microsoft.Extensions.Options;

namespace ArchonAI.Enterprise.Tests.Security;

/// <summary>
/// Security tests for detecting insecure default configurations:
/// overly permissive policies, weak thresholds, missing rate limits,
/// and unsafe default tenancy settings.
/// </summary>
public sealed class InsecureConfigurationTests
{
    // ── Policy Defaults ───────────────────────────────────────────────

    [Fact]
    public void PolicyDefaults_ForbiddenCapabilities_DefaultsToEmpty()
    {
        var opts = new PolicyOptions();

        // Empty forbidden list is acceptable for defaults, but must be configurable
        Assert.NotNull(opts.ForbiddenCapabilities);
    }

    [Fact]
    public void PolicyDefaults_HighRiskCapabilities_AreConfigured()
    {
        var opts = new PolicyOptions();

        Assert.NotEmpty(opts.HighRiskCapabilities);
        Assert.Contains("financial-operations", opts.HighRiskCapabilities);
        Assert.Contains("external.connector", opts.HighRiskCapabilities);
    }

    [Fact]
    public void PolicyDefaults_ConfidenceThreshold_IsReasonable()
    {
        var opts = new PolicyOptions();

        // Threshold should be high enough to catch low-confidence decisions
        Assert.True(opts.MinConfidenceThreshold >= 0.5,
            $"Confidence threshold {opts.MinConfidenceThreshold} is dangerously low");
        Assert.True(opts.MinConfidenceThreshold <= 1.0);
    }

    [Fact]
    public void PolicyDefaults_AutoBlockThreshold_IsSet()
    {
        var opts = new PolicyOptions();

        Assert.True(opts.AutoBlockRiskThreshold > 0,
            "AutoBlockRiskThreshold must be > 0 to prevent unbounded risk execution");
        Assert.True(opts.AutoBlockRiskThreshold <= 100);
    }

    [Fact]
    public void PolicyDefaults_ApprovalThreshold_IsSet()
    {
        var opts = new PolicyOptions();

        Assert.True(opts.ApprovalRiskThreshold > 0,
            "ApprovalRiskThreshold must be > 0 to gate risky operations");
        Assert.True(opts.ApprovalRiskThreshold < opts.AutoBlockRiskThreshold,
            "Approval threshold should be lower than auto-block threshold");
    }

    [Fact]
    public void PolicyDefaults_RequiresApprovalForHighRisk()
    {
        var opts = new PolicyOptions();

        Assert.True(opts.RequireApprovalCheckpointForHighRisk,
            "High-risk operations must require approval by default");
    }

    [Fact]
    public void PolicyDefaults_InputCountLimit_IsReasonable()
    {
        var opts = new PolicyOptions();

        Assert.True(opts.MaxTaskInputCount > 0,
            "MaxTaskInputCount must be positive");
        Assert.True(opts.MaxTaskInputCount <= 1000,
            $"MaxTaskInputCount {opts.MaxTaskInputCount} is unreasonably large");
    }

    // ── Multi-Tenant Defaults ─────────────────────────────────────────

    [Fact]
    public void TenantDefaults_MaxConcurrentPlans_IsLimited()
    {
        var opts = new MultiTenantOptions();

        Assert.True(opts.MaxConcurrentPlansPerTenant > 0,
            "Max concurrent plans must be positive");
        Assert.True(opts.MaxConcurrentPlansPerTenant <= 100,
            $"Max concurrent plans {opts.MaxConcurrentPlansPerTenant} is very high");
    }

    [Fact]
    public void TenantDefaults_MaxTasksPerPlan_IsLimited()
    {
        var opts = new MultiTenantOptions();

        Assert.True(opts.MaxTasksPerPlanPerTenant > 0,
            "Max tasks per plan must be positive");
        Assert.True(opts.MaxTasksPerPlanPerTenant <= 10000,
            $"Max tasks per plan {opts.MaxTasksPerPlanPerTenant} is very high");
    }

    [Fact]
    public void TenantDefaults_MaxMemoryRecords_IsLimited()
    {
        var opts = new MultiTenantOptions();

        Assert.True(opts.MaxMemoryRecordsPerScopePerTenant > 0,
            "Max memory records must be positive");
        Assert.True(opts.MaxMemoryRecordsPerScopePerTenant <= 50000,
            $"Max memory records {opts.MaxMemoryRecordsPerScopePerTenant} is very high");
    }

    [Fact]
    public void TenantDefaults_DefaultTenantId_IsNotEmpty()
    {
        var opts = new MultiTenantOptions();

        Assert.False(string.IsNullOrWhiteSpace(opts.DefaultTenantId),
            "Default tenant ID must not be empty");
    }

    // ── Governance Policy Defaults ────────────────────────────────────

    [Fact]
    public async Task GovernanceDefaults_CriticalActions_RequireApproval()
    {
        var gov = new Api.Security.GovernanceService();

        Assert.True(await gov.RequiresApprovalAsync("workflow.cancel"),
            "workflow.cancel must require approval");
        Assert.True(await gov.RequiresApprovalAsync("policy.delete"),
            "policy.delete must require approval");
        Assert.True(await gov.RequiresApprovalAsync("connector.disconnect"),
            "connector.disconnect must require approval");
        Assert.True(await gov.RequiresApprovalAsync("strategy.override"),
            "strategy.override must require approval");
    }

    [Fact]
    public async Task GovernanceDefaults_UnknownActions_DoNotRequireApproval()
    {
        var gov = new Api.Security.GovernanceService();

        Assert.False(await gov.RequiresApprovalAsync("agents.read"),
            "Read operations should not require approval");
        Assert.False(await gov.RequiresApprovalAsync("random.action"),
            "Unconfigured actions should not require approval");
    }
}
