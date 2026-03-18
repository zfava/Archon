using ArchonAI.Core.Models;
using ArchonAI.Policy;
using Microsoft.Extensions.Options;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Integration tests for PolicyEngine risk scoring and decision logic.
/// Validates: forbidden capability blocking, risk thresholds, approval gating,
/// confidence scoring, and manual override handling.
/// </summary>
public sealed class PolicyEngineIntegrationTests
{
    private static PolicyEngine CreateEngine(Action<PolicyOptions>? configure = null)
    {
        var opts = new PolicyOptions();
        configure?.Invoke(opts);
        return new PolicyEngine(Options.Create(opts));
    }

    private static Agent MakeAgent(bool enabled = true, params string[] capabilities) =>
        new(Guid.NewGuid(), "test-agent", "1.0",
            capabilities.Select(c => new AgentCapability(c, c, "general", "1.0")).ToList(),
            enabled, DateTimeOffset.UtcNow);

    private static CoreTask MakeTask(string capability = "data-read", int inputCount = 1)
    {
        var taskId = Guid.NewGuid();
        return new CoreTask(taskId, Guid.NewGuid(), 1, "Test", "desc", capability,
            Enumerable.Range(0, inputCount).ToDictionary(i => $"k{i}", i => $"v{i}"),
            DateTimeOffset.UtcNow, null, null);
    }

    private static CoreExecutionContext MakeContext(
        double? confidence = null, bool isAdmin = true,
        string? manualOverride = null, string? approvedTaskId = null)
    {
        var metadata = new Dictionary<string, string>();
        if (confidence.HasValue)
            metadata["confidence"] = confidence.Value.ToString();
        if (isAdmin)
            metadata["permissions"] = "admin,read,write";
        if (!string.IsNullOrEmpty(manualOverride))
            metadata["manualOverride"] = manualOverride;
        if (!string.IsNullOrEmpty(approvedTaskId))
            metadata["approvedTasks"] = approvedTaskId;

        return new CoreExecutionContext(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "tenant-1",
            metadata, DateTimeOffset.UtcNow);
    }

    // ── Forbidden Capability Blocking ─────────────────────────────────

    [Fact]
    public async Task ForbiddenCapability_BlocksExecution()
    {
        var engine = CreateEngine(o => o.ForbiddenCapabilities.Add("destructive-ops"));
        var agent = MakeAgent(true, "destructive-ops");
        var task = MakeTask("destructive-ops");

        var decision = await engine.EvaluateAsync(agent, task, MakeContext());

        Assert.False(decision.IsAllowed);
        Assert.True(decision.RiskScore >= 100);
        Assert.Contains(decision.GuardrailViolations, v => v.Contains("forbidden", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NonForbiddenCapability_AllowsExecution()
    {
        var engine = CreateEngine(o => o.ForbiddenCapabilities.Add("destructive-ops"));
        var agent = MakeAgent(true, "data-read");
        var task = MakeTask("data-read");

        var decision = await engine.EvaluateAsync(agent, task, MakeContext(confidence: 0.9));

        Assert.True(decision.IsAllowed);
        Assert.Empty(decision.GuardrailViolations);
    }

    // ── Disabled Agent ────────────────────────────────────────────────

    [Fact]
    public async Task DisabledAgent_IsBlocked()
    {
        var engine = CreateEngine();
        var agent = MakeAgent(enabled: false, "data-read");
        var task = MakeTask("data-read");

        var decision = await engine.EvaluateAsync(agent, task, MakeContext(confidence: 0.9));

        Assert.False(decision.IsAllowed);
    }

    // ── High-Risk Capability Scoring ──────────────────────────────────

    [Fact]
    public async Task HighRiskCapability_IncreasesRiskScore()
    {
        var engine = CreateEngine();
        var agent = MakeAgent(true, "financial-operations");
        var task = MakeTask("financial-operations");

        var decision = await engine.EvaluateAsync(agent, task, MakeContext(confidence: 0.9));

        Assert.True(decision.RiskScore > 0);
    }

    [Fact]
    public async Task HighRiskCapability_RequiresApproval_WhenAboveThreshold()
    {
        var engine = CreateEngine(o =>
        {
            o.ApprovalRiskThreshold = 30;
            o.RequireApprovalCheckpointForHighRisk = true;
        });
        var agent = MakeAgent(true, "financial-operations");
        var task = MakeTask("financial-operations");

        var decision = await engine.EvaluateAsync(agent, task, MakeContext(confidence: 0.9));

        Assert.True(decision.RequiresApproval);
    }

    // ── Input Count Validation ────────────────────────────────────────

    [Fact]
    public async Task ExcessiveInputs_IncreasesRisk()
    {
        var engine = CreateEngine(o => o.MaxTaskInputCount = 5);
        var agent = MakeAgent(true, "data-read");
        var task = MakeTask("data-read", inputCount: 10);

        var decision = await engine.EvaluateAsync(agent, task, MakeContext(confidence: 0.9));

        Assert.True(decision.RiskScore > 0);
    }

    // ── Low Confidence Handling ───────────────────────────────────────

    [Fact]
    public async Task LowConfidence_IncreasesRisk()
    {
        var engine = CreateEngine(o => o.MinConfidenceThreshold = 0.75);
        var agent = MakeAgent(true, "data-read");
        var task = MakeTask("data-read");

        var decision = await engine.EvaluateAsync(agent, task, MakeContext(confidence: 0.3));

        Assert.True(decision.RiskScore > 0);
    }

    // ── Manual Override ───────────────────────────────────────────────

    [Fact]
    public async Task ManualOverrideDeny_BlocksExecution()
    {
        var engine = CreateEngine();
        var agent = MakeAgent(true, "data-read");
        var task = MakeTask("data-read");

        var decision = await engine.EvaluateAsync(agent, task,
            MakeContext(confidence: 0.9, manualOverride: "deny"));

        Assert.False(decision.IsAllowed);
        Assert.Equal("deny", decision.ManualOverrideState);
    }

    [Fact]
    public async Task ManualOverrideAllow_PermitsExecution()
    {
        var engine = CreateEngine();
        var agent = MakeAgent(true, "data-read");
        var task = MakeTask("data-read");

        var decision = await engine.EvaluateAsync(agent, task,
            MakeContext(confidence: 0.9, manualOverride: "allow"));

        Assert.True(decision.IsAllowed);
        Assert.Equal("allow", decision.ManualOverrideState);
    }

    // ── Auto-Block Threshold ──────────────────────────────────────────

    [Fact]
    public async Task RiskAboveAutoBlockThreshold_IsBlocked()
    {
        var engine = CreateEngine(o =>
        {
            o.AutoBlockRiskThreshold = 50;
            o.ForbiddenCapabilities.Add("nuclear-launch");
        });
        var agent = MakeAgent(true, "nuclear-launch");
        var task = MakeTask("nuclear-launch");

        var decision = await engine.EvaluateAsync(agent, task, MakeContext(confidence: 0.9));

        Assert.False(decision.IsAllowed);
        Assert.True(decision.RiskScore >= 50);
    }

    // ── Compound Risk Stacking ────────────────────────────────────────

    [Fact]
    public async Task MultipleRiskFactors_Stack()
    {
        var engine = CreateEngine(o =>
        {
            o.MaxTaskInputCount = 5;
            o.MinConfidenceThreshold = 0.9;
        });
        var agent = MakeAgent(true, "financial-operations");
        var task = MakeTask("financial-operations", inputCount: 20);

        var decision = await engine.EvaluateAsync(agent, task,
            MakeContext(confidence: 0.2, isAdmin: false));

        // High-risk capability (40) + excess inputs (35) + low confidence
        Assert.True(decision.RiskScore >= 75);
    }

    // ── Decision Metadata ─────────────────────────────────────────────

    [Fact]
    public async Task Decision_IncludesTimestamp()
    {
        var engine = CreateEngine();
        var agent = MakeAgent(true, "data-read");
        var task = MakeTask("data-read");

        var before = DateTimeOffset.UtcNow;
        var decision = await engine.EvaluateAsync(agent, task, MakeContext(confidence: 0.9));

        Assert.True(decision.EvaluatedAtUtc >= before);
        Assert.True(decision.EvaluatedAtUtc <= DateTimeOffset.UtcNow.AddSeconds(5));
    }

    // ── Approval Checkpoint ───────────────────────────────────────────

    [Fact]
    public async Task HighRiskWithoutCheckpoint_RequiresApproval()
    {
        var engine = CreateEngine(o =>
        {
            o.RequireApprovalCheckpointForHighRisk = true;
        });
        var agent = MakeAgent(true, "financial-operations");
        var task = MakeTask("financial-operations");

        var decision = await engine.EvaluateAsync(agent, task, MakeContext(confidence: 0.9));

        Assert.True(decision.RequiresApproval);
        Assert.Contains("checkpoint", decision.ApprovalState, StringComparison.OrdinalIgnoreCase);
    }
}
