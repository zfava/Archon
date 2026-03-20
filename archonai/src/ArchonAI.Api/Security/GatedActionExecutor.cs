using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Governance;
using ArchonAI.Core.Models.HumanOverride;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Api.Security;

/// <summary>
/// Dispatches execution of approved governance-gated actions based on stored action intent.
/// </summary>
public sealed class GatedActionExecutor : IGatedActionExecutor
{
    private readonly IAdminService _adminService;
    private readonly IHumanOverrideService _overrideService;
    private readonly IEventBus _eventBus;
    private readonly ILogger<GatedActionExecutor> _logger;

    public GatedActionExecutor(
        IAdminService adminService,
        IHumanOverrideService overrideService,
        IEventBus eventBus,
        ILogger<GatedActionExecutor> logger)
    {
        _adminService = adminService;
        _overrideService = overrideService;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<GatedActionResult> ExecuteAsync(ApprovalGate gate, CancellationToken ct = default)
    {
        if (gate.Status != ApprovalStatus.Approved)
            return new GatedActionResult(false, "Gate is not approved.");

        if (gate.ActionPayload is null)
            return new GatedActionResult(false, "No action payload stored.");

        var result = gate.ActionType switch
        {
            "workflow.cancel" => await ExecuteWorkflowCancelAsync(gate.ActionPayload, ct),
            "connector.disconnect" => await ExecuteConnectorDisconnectAsync(gate.ActionPayload, ct),
            "strategy.override" => await ExecuteStrategyOverrideAsync(gate.ActionPayload, ct),
            _ => new GatedActionResult(false, $"Unknown action type '{gate.ActionType}'.")
        };

        // Publish action execution event for proof auto-emission
        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "gated-action.executed",
            nameof(GatedActionExecutor),
            gate.Id,
            new Dictionary<string, string>
            {
                ["actionType"] = gate.ActionType,
                ["tenantId"] = gate.TenantId,
                ["success"] = result.Success.ToString(),
                ["detail"] = result.Error ?? "",
                ["actor"] = gate.ReviewedBy ?? "system",
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);

        return result;
    }

    private async Task<GatedActionResult> ExecuteWorkflowCancelAsync(string payload, CancellationToken ct)
    {
        var intent = JsonSerializer.Deserialize<WorkflowCancelIntent>(payload);
        if (intent?.WorkflowId is null)
            return new GatedActionResult(false, "Invalid workflow cancel payload.");

        await _adminService.CancelWorkflowAsync(Guid.Parse(intent.WorkflowId), ct);
        return new GatedActionResult(true);
    }

    private global::System.Threading.Tasks.Task<GatedActionResult> ExecuteConnectorDisconnectAsync(string payload, CancellationToken ct)
    {
        // Connector disconnect is currently a no-op acknowledgment in the endpoint.
        // The gate approval itself authorises the disconnect.
        return global::System.Threading.Tasks.Task.FromResult(new GatedActionResult(true));
    }

    private async global::System.Threading.Tasks.Task<GatedActionResult> ExecuteStrategyOverrideAsync(string payload, CancellationToken ct)
    {
        var req = JsonSerializer.Deserialize<ModifyStrategyRequest>(payload);
        if (req is null)
            return new GatedActionResult(false, "Invalid strategy override payload.");

        var result = await _overrideService.ModifyStrategyAsync(req, ct);
        return result.Success
            ? new GatedActionResult(true)
            : new GatedActionResult(false, $"Strategy override failed: {result.Message}");
    }

    // Intent DTOs for deserialization
    private sealed record WorkflowCancelIntent(string? WorkflowId);
}
