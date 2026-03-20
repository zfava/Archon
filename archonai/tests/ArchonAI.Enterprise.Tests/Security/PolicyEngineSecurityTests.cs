using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Policy;
using ArchonAI.Policy.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Enterprise.Tests.Security;

/// <summary>
/// Security-focused tests for PolicyEngine: manual override bypass prevention,
/// expired token rejection, signed token acceptance, confidence defaults, and
/// forbidden capability resistance even with valid overrides.
/// </summary>
public sealed class PolicyEngineSecurityTests
{
    private const string TestSigningKey = "test-override-signing-key-for-security-tests-32chars!";

    private static PolicyEngine CreateEngine(Action<PolicyOptions>? configure = null)
    {
        var opts = new PolicyOptions();
        configure?.Invoke(opts);
        return new PolicyEngine(Options.Create(opts), Substitute.For<IEventBus>(), NullLogger<PolicyEngine>.Instance);
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
        Dictionary<string, string>? extraMetadata = null)
    {
        var metadata = new Dictionary<string, string>();
        if (confidence.HasValue)
            metadata["confidence"] = confidence.Value.ToString();
        if (isAdmin)
            metadata["permissions"] = "admin,read,write";
        if (extraMetadata is not null)
        {
            foreach (var kv in extraMetadata)
                metadata[kv.Key] = kv.Value;
        }
        return new CoreExecutionContext(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "tenant-1",
            metadata, DateTimeOffset.UtcNow);
    }

    // ── Test 1: Plaintext manualOverride key does NOT bypass policy ──────

    [Fact]
    public async Task ManualOverride_WithNoToken_DoesNotBypassPolicy()
    {
        var engine = CreateEngine(o =>
        {
            o.ForbiddenCapabilities.Add("dangerous-op");
            o.ManualOverrideSigningKey = TestSigningKey;
        });
        var agent = MakeAgent(true, "dangerous-op");
        var task = MakeTask("dangerous-op");

        // Inject plaintext "manualOverride" key (not a signed token) — should be ignored
        var ctx = MakeContext(confidence: 0.9, extraMetadata: new Dictionary<string, string>
        {
            ["manualOverride"] = "allow"
        });

        var decision = await engine.EvaluateAsync(agent, task, ctx);

        Assert.False(decision.IsAllowed);
        Assert.Equal("none", decision.ManualOverrideState);
        Assert.Contains("forbidden-capability", decision.GuardrailViolations);
    }

    // ── Test 2: Expired signed token is rejected ────────────────────────

    [Fact]
    public async Task ManualOverride_WithExpiredToken_IsRejected()
    {
        var engine = CreateEngine(o => o.ManualOverrideSigningKey = TestSigningKey);
        var agent = MakeAgent(true, "data-read");
        var task = MakeTask("data-read");

        // Create a token that expired 1 hour ago
        var expiredToken = new ManualOverrideToken(
            TokenId: Guid.NewGuid(),
            Action: "allow",
            TargetTaskId: task.Id.ToString(),
            TargetObjectiveId: Guid.Empty.ToString(),
            AuthorizedBy: "test-admin",
            AuthorizedByRole: "admin",
            IssuedAtUtc: DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(-1));
        var signedExpired = ManualOverrideTokenService.IssueToken(expiredToken, TestSigningKey);

        var ctx = MakeContext(confidence: 0.9, extraMetadata: new Dictionary<string, string>
        {
            ["manualOverrideToken"] = signedExpired
        });

        var decision = await engine.EvaluateAsync(agent, task, ctx);

        // Expired token should be treated as "none" — policy evaluates normally
        Assert.Equal("none", decision.ManualOverrideState);
    }

    // ── Test 3: Valid signed token allows execution ─────────────────────

    [Fact]
    public async Task ManualOverride_WithValidSignedToken_AllowsExecution()
    {
        var engine = CreateEngine(o => o.ManualOverrideSigningKey = TestSigningKey);
        var agent = MakeAgent(true, "data-read");
        var task = MakeTask("data-read");

        var validToken = new ManualOverrideToken(
            TokenId: Guid.NewGuid(),
            Action: "allow",
            TargetTaskId: task.Id.ToString(),
            TargetObjectiveId: Guid.Empty.ToString(),
            AuthorizedBy: "security-admin",
            AuthorizedByRole: "admin",
            IssuedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(30));
        var signed = ManualOverrideTokenService.IssueToken(validToken, TestSigningKey);

        var ctx = MakeContext(confidence: 0.9, extraMetadata: new Dictionary<string, string>
        {
            ["manualOverrideToken"] = signed
        });

        var decision = await engine.EvaluateAsync(agent, task, ctx);

        Assert.True(decision.IsAllowed);
        Assert.Equal("allow", decision.ManualOverrideState);
        Assert.Equal("overridden-approved", decision.ApprovalState);
    }

    // ── Test 4: Default confidence below threshold requires approval ────

    [Fact]
    public async Task ConfidenceDefault_BelowThreshold_RequiresApproval()
    {
        var engine = CreateEngine(o =>
        {
            o.DefaultConfidenceScore = 0.3;
            o.MinConfidenceThreshold = 0.65;
            o.ApprovalRiskThreshold = 10;
        });
        var agent = MakeAgent(true, "data-read");
        var task = MakeTask("data-read");

        // No confidence in context or task inputs → uses DefaultConfidenceScore (0.3)
        var ctx = MakeContext(isAdmin: true);

        var decision = await engine.EvaluateAsync(agent, task, ctx);

        Assert.Contains("confidence-below-threshold", decision.GuardrailViolations);
        Assert.True(decision.RiskScore > 0);
        Assert.True(decision.RequiresApproval);
        Assert.Equal(0.3, decision.ConfidenceScore);
    }

    // ── Test 5: Forbidden capability always blocked even with valid override ─

    [Fact]
    public async Task ForbiddenCapability_AlwaysBlocked_EvenWithManualOverride()
    {
        var engine = CreateEngine(o =>
        {
            o.ForbiddenCapabilities.Add("self-destruct");
            o.ManualOverrideSigningKey = TestSigningKey;
        });
        var agent = MakeAgent(true, "self-destruct");
        var task = MakeTask("self-destruct");

        // Issue a perfectly valid signed override token
        var overrideToken = new ManualOverrideToken(
            TokenId: Guid.NewGuid(),
            Action: "allow",
            TargetTaskId: task.Id.ToString(),
            TargetObjectiveId: Guid.Empty.ToString(),
            AuthorizedBy: "root-admin",
            AuthorizedByRole: "admin",
            IssuedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(30));
        var signed = ManualOverrideTokenService.IssueToken(overrideToken, TestSigningKey);

        var ctx = MakeContext(confidence: 0.9, extraMetadata: new Dictionary<string, string>
        {
            ["manualOverrideToken"] = signed
        });

        var decision = await engine.EvaluateAsync(agent, task, ctx);

        Assert.False(decision.IsAllowed);
        Assert.Contains("forbidden-capability", decision.GuardrailViolations);
        Assert.Contains("Forbidden capability cannot be overridden", decision.Reason);
    }
}
