using ArchonAI.Core.Models;
using ArchonAI.Policy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Tests;

public sealed class PolicyEngineDefaultConfidenceTests
{
    private static PolicyEngine CreateEngine(Action<PolicyOptions>? configure = null)
    {
        var opts = new PolicyOptions();
        configure?.Invoke(opts);
        return new PolicyEngine(Options.Create(opts), NullLogger<PolicyEngine>.Instance);
    }

    private static Agent MakeAgent(bool enabled = true) =>
        new(Guid.NewGuid(), "test-agent", "1.0",
            new List<AgentCapability>
            {
                new("data-read", "data-read", "general", "1.0")
            },
            enabled, DateTimeOffset.UtcNow);

    private static CoreTask MakeTask(string capability = "data-read", Dictionary<string, string>? inputs = null)
    {
        inputs ??= new Dictionary<string, string> { ["key"] = "value" };
        return new CoreTask(Guid.NewGuid(), Guid.NewGuid(), 1, "Test", "desc", capability,
            inputs, DateTimeOffset.UtcNow, null, null);
    }

    private static CoreExecutionContext MakeContext(Dictionary<string, string>? metadata = null)
    {
        metadata ??= new Dictionary<string, string>();
        return new CoreExecutionContext(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "tenant-1",
            metadata, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task NoConfidenceSignal_UsesConfiguredDefault()
    {
        var engine = CreateEngine(o =>
        {
            o.DefaultConfidenceScore = 0.5;
            o.MinConfidenceThreshold = 0.4; // below default so it passes
        });

        var decision = await engine.EvaluateAsync(
            MakeAgent(),
            MakeTask(),
            MakeContext());

        Assert.Equal(0.5, decision.ConfidenceScore);
        Assert.DoesNotContain("confidence-below-threshold", decision.GuardrailViolations);
    }

    [Fact]
    public async Task DefaultConfidenceBelowThreshold_RequiresApproval()
    {
        var engine = CreateEngine(o =>
        {
            o.DefaultConfidenceScore = 0.3;
            o.MinConfidenceThreshold = 0.65;
            o.ConfidenceRiskWeight = 50;
            o.ApprovalRiskThreshold = 10; // low threshold so confidence risk triggers approval
        });

        var decision = await engine.EvaluateAsync(
            MakeAgent(),
            MakeTask(),
            MakeContext());

        // Default 0.3 < threshold 0.65 => confidence-below-threshold violation
        Assert.Contains("confidence-below-threshold", decision.GuardrailViolations);
        Assert.True(decision.RequiresApproval);
        // Risk should be (0.65 - 0.3) * 50 = 17.5
        Assert.True(decision.RiskScore >= 17);
    }

    [Fact]
    public async Task ExplicitContextConfidence_OverridesDefault()
    {
        var engine = CreateEngine(o =>
        {
            o.DefaultConfidenceScore = 0.3; // low default
            o.MinConfidenceThreshold = 0.65;
        });

        var metadata = new Dictionary<string, string> { ["confidence"] = "0.9" };

        var decision = await engine.EvaluateAsync(
            MakeAgent(),
            MakeTask(),
            MakeContext(metadata));

        // Explicit 0.9 should be used, not the 0.3 default
        Assert.Equal(0.9, decision.ConfidenceScore);
        Assert.DoesNotContain("confidence-below-threshold", decision.GuardrailViolations);
    }
}
