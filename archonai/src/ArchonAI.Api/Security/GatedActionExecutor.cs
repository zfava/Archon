using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Governance;
using ArchonAI.Core.Models.HumanOverride;

namespace ArchonAI.Api.Security;

/// <summary>
/// Dispatches execution of approved governance-gated actions based on stored action intent.
/// </summary>
public sealed class GatedActionExecutor : IGatedActionExecutor
{
    private readonly IAdminService _adminService;
    private readonly IHumanOverrideService _overrideService;

    public GatedActionExecutor(IAdminService adminService, IHumanOverrideService overrideService)
    {
        _adminService = adminService;
        _overrideService = overrideService;
    }

    public async Task<GatedActionResult> ExecuteAsync(ApprovalGate gate, CancellationToken ct = default)
    {
        if (gate.Status != ApprovalStatus.Approved)
            return new GatedActionResult(false, "Gate is not approved.");

        if (gate.ActionPayload is null)
            return new GatedActionResult(false, "No action payload stored.");

        return gate.ActionType switch
        {
            "workflow.cancel" => await ExecuteWorkflowCancelAsync(gate.ActionPayload, ct),
            "connector.disconnect" => await ExecuteConnectorDisconnectAsync(gate.ActionPayload, ct),
            "strategy.override" => await ExecuteStrategyOverrideAsync(gate.ActionPayload, ct),
            _ => new GatedActionResult(false, $"Unknown action type '{gate.ActionType}'.")
        };
    }

    private async Task<GatedActionResult> ExecuteWorkflowCancelAsync(string payload, CancellationToken ct)
    {
        var intent = JsonSerializer.Deserialize<WorkflowCancelIntent>(payload);
        if (intent?.WorkflowId is null)
            return new GatedActionResult(false, "Invalid workflow cancel payload.");

        await _adminService.CancelWorkflowAsync(Guid.Parse(intent.WorkflowId), ct);
        return new GatedActionResult(true);
    }

    private Task<GatedActionResult> ExecuteConnectorDisconnectAsync(string payload, CancellationToken ct)
    {
        // Connector disconnect is currently a no-op acknowledgment in the endpoint.
        // The gate approval itself authorises the disconnect.
        return Task.FromResult(new GatedActionResult(true));
    }

    private async Task<GatedActionResult> ExecuteStrategyOverrideAsync(string payload, CancellationToken ct)
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
