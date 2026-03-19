using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Policy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Policy;

public sealed class PolicyEngine : IPolicyEngine
{
    private readonly PolicyOptions _options;
    private readonly ILogger<PolicyEngine> _logger;

    public PolicyEngine(IOptions<PolicyOptions> options, ILogger<PolicyEngine> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public global::System.Threading.Tasks.Task<PolicyDecision> EvaluateAsync(
        Agent agent,
        CoreTask task,
        CoreExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var violations = new List<string>();
        double risk = 0;

        if (_options.ForbiddenCapabilities.Any(c => c.Equals(task.RequiredCapability, StringComparison.OrdinalIgnoreCase)))
        {
            violations.Add("forbidden-capability");
            risk += 100;
        }

        if (task.Inputs.Count > Math.Max(1, _options.MaxTaskInputCount))
        {
            violations.Add("input-count-exceeded");
            risk += 35;
        }

        bool isHighRiskCapability = _options.HighRiskCapabilities.Any(c => c.Equals(task.RequiredCapability, StringComparison.OrdinalIgnoreCase));
        if (isHighRiskCapability)
        {
            risk += 40;
        }

        if (!agent.IsEnabled)
        {
            violations.Add("agent-disabled");
            risk += 100;
        }

        if (task.RequiredCapability.Contains("admin", StringComparison.OrdinalIgnoreCase))
        {
            bool hasAdmin = context.Metadata.TryGetValue("permissions", out string? permissions)
                && permissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(p => p.Equals("admin", StringComparison.OrdinalIgnoreCase));

            if (!hasAdmin)
            {
                violations.Add("missing-admin-permission");
                risk += 60;
            }
        }

        double confidenceScore = ResolveConfidenceScore(task, context);
        if (confidenceScore < _options.MinConfidenceThreshold)
        {
            violations.Add("confidence-below-threshold");
            risk += (_options.MinConfidenceThreshold - confidenceScore) * _options.ConfidenceRiskWeight;
        }

        bool requiresApproval = risk >= _options.ApprovalRiskThreshold;
        string approvalState = "not-required";

        bool requiresCheckpoint = _options.ApprovalCheckpointCapabilities.Any(c => c.Equals(task.RequiredCapability, StringComparison.OrdinalIgnoreCase))
            || (_options.RequireApprovalCheckpointForHighRisk && isHighRiskCapability);

        string approvalCheckpoint = requiresCheckpoint ? _options.DefaultApprovalCheckpoint : "none";
        if (requiresCheckpoint)
        {
            bool checkpointPassed = context.Metadata.TryGetValue("approvalCheckpointPassed", out string? checkpoint)
                && bool.TryParse(checkpoint, out bool parsed)
                && parsed;

            if (!checkpointPassed)
            {
                requiresApproval = true;
                violations.Add("approval-checkpoint-required");
                approvalState = "checkpoint-pending";
            }
            else
            {
                approvalState = "checkpoint-passed";
            }
        }

        if (requiresApproval)
        {
            bool approved = context.Metadata.TryGetValue("approvedTasks", out string? approvedTasks)
                && approvedTasks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(id => id.Equals(task.Id.ToString(), StringComparison.OrdinalIgnoreCase)
                           || id.Equals(task.ObjectiveId.ToString(), StringComparison.OrdinalIgnoreCase));

            if (approvalState == "not-required")
            {
                approvalState = approved ? "approved" : "pending";
            }

            if (!approved && !approvalState.Equals("checkpoint-pending", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add("approval-required");
            }
        }

        string manualOverrideState = ResolveManualOverrideState(task, context);

        bool isAllowed = violations.Count == 0 && risk < _options.AutoBlockRiskThreshold;
        string reason = isAllowed
            ? "Policy checks passed."
            : $"Policy denied task due to: {string.Join(',', violations)}";

        if (manualOverrideState == "deny")
        {
            isAllowed = false;
            requiresApproval = false;
            approvalState = "overridden-denied";
            reason = "Execution denied by manual human override.";
            if (!violations.Contains("manual-override-deny", StringComparer.OrdinalIgnoreCase))
            {
                violations.Add("manual-override-deny");
            }
        }
        else if (manualOverrideState == "allow")
        {
            isAllowed = true;
            requiresApproval = false;
            approvalState = "overridden-approved";
            reason = "Execution allowed by manual human override.";
        }

        return global::System.Threading.Tasks.Task.FromResult(new PolicyDecision(
            IsAllowed: isAllowed,
            RiskScore: Math.Clamp(risk, 0, 100),
            ConfidenceScore: Math.Clamp(confidenceScore, 0, 1),
            RequiresApproval: requiresApproval,
            ApprovalState: approvalState,
            ManualOverrideState: manualOverrideState,
            ApprovalCheckpoint: approvalCheckpoint,
            GuardrailViolations: violations,
            Reason: reason,
            EvaluatedAtUtc: DateTimeOffset.UtcNow));
    }

    private string ResolveManualOverrideState(CoreTask task, CoreExecutionContext context)
    {
        if (!context.Metadata.TryGetValue("manualOverrideToken", out string? tokenString)
            || string.IsNullOrWhiteSpace(tokenString))
        {
            return "none";
        }

        if (string.IsNullOrWhiteSpace(_options.ManualOverrideSigningKey))
        {
            _logger.LogWarning(
                "Manual override token presented for task {TaskId} but ManualOverrideSigningKey is not configured — rejecting",
                task.Id);
            return "none";
        }

        var token = ManualOverrideTokenService.ValidateToken(tokenString, _options.ManualOverrideSigningKey);
        if (token is null)
        {
            _logger.LogWarning(
                "Invalid or expired manual override token presented for task {TaskId}",
                task.Id);
            return "none";
        }

        // Verify the token targets this specific task
        if (!string.Equals(token.TargetTaskId, task.Id.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Manual override token target task {TokenTaskId} does not match current task {TaskId} — rejecting",
                token.TargetTaskId, task.Id);
            return "none";
        }

        _logger.LogInformation(
            "Valid manual override token accepted: action={Action} task={TaskId} authorizedBy={AuthorizedBy} role={Role}",
            token.Action, task.Id, token.AuthorizedBy, token.AuthorizedByRole);

        return token.Action.ToLowerInvariant();
    }

    private double ResolveConfidenceScore(CoreTask task, CoreExecutionContext context)
    {
        if (context.Metadata.TryGetValue("confidence", out string? contextConfidence)
            && double.TryParse(contextConfidence, out double parsedContextConfidence))
        {
            return parsedContextConfidence;
        }

        if (task.Inputs.TryGetValue("confidence", out string? taskConfidence)
            && double.TryParse(taskConfidence, out double parsedTaskConfidence))
        {
            return parsedTaskConfidence;
        }

        _logger.LogDebug(
            "No confidence signal found for task {TaskId}. Using configured default {Default}. " +
            "Set 'confidence' in context.Metadata or task.Inputs to override.",
            task.Id, _options.DefaultConfidenceScore);

        return _options.DefaultConfidenceScore;
    }
}
