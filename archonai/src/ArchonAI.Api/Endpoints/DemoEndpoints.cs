using System.Diagnostics;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Governance;
using ArchonAI.Core.Models.Models;
using ArchonAI.Core.Models.Policy;
using ArchonAI.Models;
using ArchonAI.Policy;
using ArchonAI.Policy.Models;
using Microsoft.Extensions.Options;
using CoreTask = ArchonAI.Core.Models.Task;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;

namespace ArchonAI.Api.Endpoints;

public static class DemoEndpoints
{
    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder v1)
    {
        var demo = v1.MapGroup("/demo")
            .WithTags("demo");

        demo.MapPost("/governance-loop", async (
            DemoRequest request,
            IModelProvider modelProvider,
            IPolicyEngine policyEngine,
            IGovernanceService governanceService,
            IGatedActionExecutor gatedActionExecutor,
            IOutcomeLearningService outcomeLearningService,
            AiRuntimeDiagnostics diagnostics,
            IOptions<DemoOptions> demoOptions,
            IOptions<PolicyOptions> policyOptions,
            ILogger<DemoOptions> logger,
            CancellationToken ct) =>
        {
            var options = demoOptions.Value;
            if (!options.Enabled)
            {
                return Results.BadRequest(new { error = "Demo mode is disabled. Set Demo:Enabled=true in configuration." });
            }

            if (Interlocked.Read(ref DemoState.ActiveDemos) >= options.MaxConcurrentDemos)
            {
                return Results.StatusCode(429);
            }

            Interlocked.Increment(ref DemoState.ActiveDemos);
            try
            {
                return Results.Ok(await RunGovernanceDemoAsync(
                    request, modelProvider, policyEngine, governanceService,
                    gatedActionExecutor, outcomeLearningService, diagnostics,
                    policyOptions.Value, logger, ct));
            }
            finally
            {
                Interlocked.Decrement(ref DemoState.ActiveDemos);
            }
        }).AllowAnonymous();

        return v1;
    }

    private static async Task<DemoResult> RunGovernanceDemoAsync(
        DemoRequest request,
        IModelProvider modelProvider,
        IPolicyEngine policyEngine,
        IGovernanceService governanceService,
        IGatedActionExecutor gatedActionExecutor,
        IOutcomeLearningService outcomeLearningService,
        AiRuntimeDiagnostics diagnostics,
        PolicyOptions policyOptions,
        ILogger logger,
        CancellationToken ct)
    {
        var totalSw = Stopwatch.StartNew();
        var phases = new List<DemoPhase>();
        var scenario = request.Scenario ?? "finance-approval";
        var trustTierThreshold = request.TrustTierThreshold ?? 500000;

        var scenarioPrompt = scenario switch
        {
            "sales-anomaly" => "Analyze the following sales anomaly: Q3 revenue shows a 35% spike in the EMEA region with no corresponding marketing spend increase. Provide a structured risk assessment with confidence score.",
            "ops-escalation" => "An operations escalation has been triggered: three consecutive deployment failures in the production pipeline with increasing latency. Assess the risk, recommend immediate actions, and estimate recovery time.",
            _ => $"Evaluate this financial approval request: A purchase order for ${trustTierThreshold:N0} has been submitted for enterprise software licensing. Assess risk, compliance requirements, and provide an approval recommendation with confidence score."
        };

        // ── Phase 1: AI Reasoning via IModelProvider ──
        var phase1Sw = Stopwatch.StartNew();
        ModelResponse? aiResponse = null;
        string aiReasoning;
        try
        {
            var modelRequest = new ModelRequest(
                Model: "openai.gpt-4.1-mini",
                Prompt: scenarioPrompt,
                Parameters: new Dictionary<string, string>
                {
                    ["scenario"] = scenario,
                    ["threshold"] = trustTierThreshold.ToString()
                },
                RequestedBy: "DemoEndpoint",
                RequestedAtUtc: DateTimeOffset.UtcNow)
            {
                SystemPrompt = "You are an enterprise AI governance analyst. Provide structured risk assessments with confidence scores between 0.0 and 1.0. Be concise.",
                MaxTokens = 500,
                Temperature = 0.3
            };

            aiResponse = await modelProvider.GenerateAsync(modelRequest, ct);
            aiReasoning = aiResponse.IsSuccess
                ? aiResponse.Content
                : $"AI provider returned error: {string.Join("; ", aiResponse.Errors)}";
        }
        catch (Exception ex)
        {
            aiReasoning = $"AI provider unavailable: {ex.Message}";
        }
        phase1Sw.Stop();

        phases.Add(new DemoPhase("ai-reasoning", aiReasoning, phase1Sw.Elapsed.TotalMilliseconds));

        // ── Phase 2: Policy Engine Evaluation ──
        var phase2Sw = Stopwatch.StartNew();
        var taskId = Guid.NewGuid();
        var objectiveId = Guid.NewGuid();
        var demoTenantId = "demo-tenant";

        var agent = new Agent(
            Guid.NewGuid(), "demo-governance-agent", "1.0",
            new[] { new AgentCapability("financial-analysis", "Financial analysis and approval", "finance", "1.0") },
            IsEnabled: true, RegisteredAtUtc: DateTimeOffset.UtcNow);

        var task = new CoreTask(
            taskId, objectiveId, 1,
            $"Demo: {scenario}",
            $"Governance demo for scenario '{scenario}' with threshold ${trustTierThreshold:N0}",
            "financial-analysis",
            new Dictionary<string, string>
            {
                ["scenario"] = scenario,
                ["amount"] = trustTierThreshold.ToString(),
                ["confidence"] = "0.55"
            },
            DateTimeOffset.UtcNow, null, null);

        var context = new CoreExecutionContext(
            Guid.NewGuid(), objectiveId, taskId, demoTenantId,
            new Dictionary<string, string>
            {
                ["confidence"] = "0.55",
                ["source"] = "demo-endpoint"
            },
            DateTimeOffset.UtcNow);

        var policyDecision = await policyEngine.EvaluateAsync(agent, task, context, ct);
        phase2Sw.Stop();

        phases.Add(new DemoPhase("policy-evaluation", System.Text.Json.JsonSerializer.Serialize(new
        {
            policyDecision.IsAllowed,
            policyDecision.RiskScore,
            policyDecision.ConfidenceScore,
            policyDecision.RequiresApproval,
            policyDecision.ApprovalState,
            policyDecision.GuardrailViolations,
            policyDecision.Reason
        }), phase2Sw.Elapsed.TotalMilliseconds));

        // ── Phase 3: Approval Gate ──
        var phase3Sw = Stopwatch.StartNew();
        Guid? approvalGateId = null;
        string approvalStatus = "not-required";
        string? overrideToken = null;

        if (policyDecision.RequiresApproval || !policyDecision.IsAllowed)
        {
            var gate = await governanceService.RequestApprovalAsync(
                actionType: $"demo-{scenario}",
                resourceId: taskId.ToString(),
                tenantId: demoTenantId,
                requestedBy: "demo-user",
                justification: $"Governance demo: {scenario} with value ${trustTierThreshold:N0}. AI confidence: {policyDecision.ConfidenceScore:F2}",
                actionPayload: System.Text.Json.JsonSerializer.Serialize(new { scenario, trustTierThreshold }),
                ct: ct);

            approvalGateId = gate.Id;
            approvalStatus = "pending";

            if (request.AutoApprove == true && !string.IsNullOrWhiteSpace(policyOptions.ManualOverrideSigningKey))
            {
                var token = new ManualOverrideToken(
                    TokenId: Guid.NewGuid(),
                    Action: "allow",
                    TargetTaskId: taskId.ToString(),
                    TargetObjectiveId: objectiveId.ToString(),
                    AuthorizedBy: "demo-auto-approver",
                    AuthorizedByRole: "Admin",
                    IssuedAtUtc: DateTimeOffset.UtcNow,
                    ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(30));

                overrideToken = ManualOverrideTokenService.IssueToken(token, policyOptions.ManualOverrideSigningKey);

                await governanceService.ReviewApprovalAsync(
                    gate.Id, demoTenantId, "demo-auto-approver", "Admin",
                    approve: true, notes: "Auto-approved via demo endpoint with HMAC-signed override token",
                    ct: ct);

                approvalStatus = "auto-approved";
            }
        }
        phase3Sw.Stop();

        phases.Add(new DemoPhase("approval-gate", System.Text.Json.JsonSerializer.Serialize(new
        {
            approvalGateId,
            status = approvalStatus,
            hasOverrideToken = overrideToken is not null
        }), phase3Sw.Elapsed.TotalMilliseconds));

        // ── Phase 4: Gated Execution ──
        var phase4Sw = Stopwatch.StartNew();
        GatedActionResult? executionResult = null;

        if (approvalGateId.HasValue && approvalStatus == "auto-approved")
        {
            var approvedGate = await governanceService.GetApprovalAsync(approvalGateId.Value, demoTenantId, ct);
            if (approvedGate is not null)
            {
                executionResult = await gatedActionExecutor.ExecuteAsync(approvedGate, ct);
                await governanceService.RecordExecutionResultAsync(
                    approvalGateId.Value,
                    executionResult.Success ? GateExecutionStatus.Succeeded : GateExecutionStatus.Failed,
                    executionResult.Error, ct);
            }
        }
        phase4Sw.Stop();

        phases.Add(new DemoPhase("gated-execution", System.Text.Json.JsonSerializer.Serialize(new
        {
            executed = executionResult is not null,
            success = executionResult?.Success,
            error = executionResult?.Error
        }), phase4Sw.Elapsed.TotalMilliseconds));

        // ── Phase 5: Outcome Recording ──
        var phase5Sw = Stopwatch.StartNew();
        var decisionId = Guid.NewGuid();
        var tenantGuid = Guid.NewGuid();

        var outcomeRecord = await outcomeLearningService.RecordExpectedOutcomeAsync(
            decisionId, tenantGuid,
            expectedSummary: $"Demo governance decision for {scenario}",
            expectedValue: trustTierThreshold,
            confidenceAtPrediction: policyDecision.ConfidenceScore,
            expectedTimeframe: "immediate",
            recordedBy: "demo-endpoint",
            ct: ct);
        phase5Sw.Stop();

        phases.Add(new DemoPhase("outcome-recording", System.Text.Json.JsonSerializer.Serialize(new
        {
            outcomeRecord.DecisionId,
            outcomeRecord.ExpectedOutcomeSummary,
            outcomeRecord.ConfidenceAtPrediction
        }), phase5Sw.Elapsed.TotalMilliseconds));

        totalSw.Stop();

        var environmentReport = diagnostics.GetEnvironmentReport();

        return new DemoResult(
            Scenario: scenario,
            Phases: phases,
            TrustTier: policyDecision.RiskScore < 40 ? "low-risk" : policyDecision.RiskScore < 70 ? "medium-risk" : "high-risk",
            ApprovalGateId: approvalGateId,
            ApprovalStatus: approvalStatus,
            OverrideToken: overrideToken,
            TrustLineageUrl: approvalGateId.HasValue
                ? $"/api/v1/trust-visibility/lineage/{approvalGateId.Value}"
                : null,
            TotalLatencyMs: totalSw.Elapsed.TotalMilliseconds,
            EnvironmentReport: new DemoEnvironmentReport(
                environmentReport.ReadinessTier,
                environmentReport.ReadinessSummary,
                environmentReport.CloudProvidersActive,
                environmentReport.LocalProviderActive,
                environmentReport.DefaultModel));
    }

    private static class DemoState
    {
        public static long ActiveDemos;
    }
}

public sealed class DemoOptions
{
    public bool Enabled { get; set; }
    public int MaxConcurrentDemos { get; set; } = 3;
    public decimal DefaultTrustTierThreshold { get; set; } = 500000;
    public string DefaultScenario { get; set; } = "finance-approval";
}

public sealed record DemoRequest(
    string? Scenario,
    decimal? TrustTierThreshold,
    bool? AutoApprove);

public sealed record DemoResult(
    string Scenario,
    IReadOnlyList<DemoPhase> Phases,
    string TrustTier,
    Guid? ApprovalGateId,
    string ApprovalStatus,
    string? OverrideToken,
    string? TrustLineageUrl,
    double TotalLatencyMs,
    DemoEnvironmentReport EnvironmentReport);

public sealed record DemoPhase(
    string Name,
    string Output,
    double LatencyMs);

public sealed record DemoEnvironmentReport(
    string ReadinessTier,
    string ReadinessSummary,
    int CloudProvidersActive,
    bool LocalProviderActive,
    string DefaultModel);
