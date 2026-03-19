using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Registry;

/// <summary>
/// Subscribes to agent lifecycle events from IAgentRegistryService (via IEventBus) and
/// synchronizes state into IAgentCapabilityRegistry, ensuring the two registries stay consistent.
/// </summary>
public sealed class AgentRegistrySynchronizer : IHostedService
{
    private readonly IEventBus _eventBus;
    private readonly IAgentCapabilityRegistry _capabilityRegistry;
    private readonly ILogger<AgentRegistrySynchronizer> _logger;

    public AgentRegistrySynchronizer(
        IEventBus eventBus,
        IAgentCapabilityRegistry capabilityRegistry,
        ILogger<AgentRegistrySynchronizer> logger)
    {
        _eventBus = eventBus;
        _capabilityRegistry = capabilityRegistry;
        _logger = logger;
    }

    public async global::System.Threading.Tasks.Task StartAsync(CancellationToken cancellationToken)
    {
        await _eventBus.SubscribeAsync("agentregistry.agent.enabled", OnAgentEnabledAsync, cancellationToken);
        await _eventBus.SubscribeAsync("agentregistry.agent.disabled", OnAgentDisabledAsync, cancellationToken);
        await _eventBus.SubscribeAsync("agentregistry.agent.deregistered", OnAgentDeregisteredAsync, cancellationToken);

        _logger.LogInformation("AgentRegistrySynchronizer started — listening for agent lifecycle events");
    }

    public global::System.Threading.Tasks.Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AgentRegistrySynchronizer stopping");
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    private async global::System.Threading.Tasks.Task OnAgentEnabledAsync(SystemEvent evt, CancellationToken ct)
    {
        if (!TryParseAgentId(evt, out var agentId)) return;

        await _capabilityRegistry.ReinstateAgentAsync(agentId, ct);
        _logger.LogInformation(
            "Synchronized: agent {AgentId} reinstated in capability registry after enable event",
            agentId);
    }

    private async global::System.Threading.Tasks.Task OnAgentDisabledAsync(SystemEvent evt, CancellationToken ct)
    {
        if (!TryParseAgentId(evt, out var agentId)) return;

        var reason = evt.Payload.TryGetValue("detail", out var detail)
            ? detail
            : "Disabled in agent registry";

        await _capabilityRegistry.SuspendAgentAsync(agentId, reason, ct);
        _logger.LogInformation(
            "Synchronized: agent {AgentId} suspended in capability registry after disable event",
            agentId);
    }

    private async global::System.Threading.Tasks.Task OnAgentDeregisteredAsync(SystemEvent evt, CancellationToken ct)
    {
        if (!TryParseAgentId(evt, out var agentId)) return;

        await _capabilityRegistry.SuspendAgentAsync(agentId, "Agent deregistered", ct);
        _logger.LogInformation(
            "Synchronized: agent {AgentId} suspended in capability registry after deregister event",
            agentId);
    }

    private bool TryParseAgentId(SystemEvent evt, out Guid agentId)
    {
        // The AgentRegistryService sets Source to the agentId string
        if (Guid.TryParse(evt.Source, out agentId))
            return true;

        _logger.LogWarning(
            "AgentRegistrySynchronizer received event {EventType} with non-GUID source: {Source}",
            evt.EventType, evt.Source);
        agentId = Guid.Empty;
        return false;
    }
}
