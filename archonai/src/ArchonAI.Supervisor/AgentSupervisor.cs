using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Supervision;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Supervisor;

public sealed class AgentSupervisor : IAgentSupervisor
{
    private sealed class AgentState
    {
        public Agent Agent { get; set; } = null!;
        public string Health { get; set; } = "healthy";
        public int ActiveExecutions { get; set; }
        public int ConsecutiveFailures { get; set; }
        public DateTimeOffset LastHeartbeatUtc { get; set; } = DateTimeOffset.UtcNow;
        public ConcurrentQueue<DateTimeOffset> ExecutionTimestamps { get; } = new();
        public ConcurrentQueue<DateTimeOffset> RestartTimestamps { get; } = new();
    }

    private readonly SupervisorOptions _options;
    private readonly ILogger<AgentSupervisor> _logger;
    private readonly ConcurrentDictionary<Guid, AgentState> _pool = new();

    public AgentSupervisor(IOptions<SupervisorOptions> options, ILogger<AgentSupervisor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public global::System.Threading.Tasks.Task RegisterAgentAsync(Agent agent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _pool.AddOrUpdate(
            agent.Id,
            _ => new AgentState
            {
                Agent = agent,
                Health = agent.IsEnabled ? "healthy" : "disabled",
                LastHeartbeatUtc = DateTimeOffset.UtcNow
            },
            (_, current) =>
            {
                current.Agent = agent;
                current.Health = agent.IsEnabled ? "healthy" : "disabled";
                current.LastHeartbeatUtc = DateTimeOffset.UtcNow;
                return current;
            });

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task<Agent?> SelectAgentAsync(
        IReadOnlyList<Agent> candidates,
        CoreTask task,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Agent? selected = candidates
            .Select(candidate => _pool.TryGetValue(candidate.Id, out AgentState? state)
                ? new { Candidate = candidate, State = state }
                : null)
            .Where(item => item is not null)
            .Select(item => item!)
            .Where(item => item.State.Health is "healthy" or "restarted")
            .OrderBy(item => item.State.ActiveExecutions)
            .ThenBy(item => item.State.LastHeartbeatUtc)
            .Select(item => item.Candidate)
            .FirstOrDefault();

        selected ??= candidates.FirstOrDefault(candidate => candidate.IsEnabled);
        return global::System.Threading.Tasks.Task.FromResult(selected);
    }

    public global::System.Threading.Tasks.Task<SupervisionDecision> ValidateExecutionAsync(
        Agent agent,
        CoreTask task,
        CoreExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var state = _pool.GetOrAdd(agent.Id, _ => new AgentState
        {
            Agent = agent,
            Health = agent.IsEnabled ? "healthy" : "disabled",
            LastHeartbeatUtc = DateTimeOffset.UtcNow
        });

        state.Agent = agent;
        var signals = new List<string>();

        if (!agent.IsEnabled || state.Health is "terminated" or "disabled")
        {
            signals.Add("agent-not-executable");
            return global::System.Threading.Tasks.Task.FromResult(new SupervisionDecision(
                IsAllowed: false,
                Reason: "Agent is disabled or terminated.",
                Action: "terminate",
                Signals: signals));
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset cutoff = now.AddSeconds(-Math.Max(1, _options.RunawayWindowSeconds));
        while (state.ExecutionTimestamps.TryPeek(out DateTimeOffset timestamp) && timestamp < cutoff)
        {
            state.ExecutionTimestamps.TryDequeue(out _);
        }

        if (state.ExecutionTimestamps.Count >= Math.Max(1, _options.MaxExecutionsInWindow))
        {
            state.Health = "terminated";
            signals.Add("runaway-loop-detected");
            _logger.LogWarning("Supervisor terminated agent {AgentId} due to runaway loop detection.", agent.Id);
            return global::System.Threading.Tasks.Task.FromResult(new SupervisionDecision(
                IsAllowed: false,
                Reason: "Runaway execution loop detected.",
                Action: "terminate",
                Signals: signals));
        }

        state.ActiveExecutions++;
        state.ExecutionTimestamps.Enqueue(now);
        state.LastHeartbeatUtc = now;
        signals.Add("health-ok");
        signals.Add("workload-balanced");

        return global::System.Threading.Tasks.Task.FromResult(new SupervisionDecision(
            IsAllowed: true,
            Reason: "Supervisor checks passed.",
            Action: "allow",
            Signals: signals));
    }

    public global::System.Threading.Tasks.Task RecordExecutionCompletedAsync(
        Agent agent,
        ExecutionResult result,
        double executionTimeMs,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_pool.TryGetValue(agent.Id, out AgentState? state))
        {
            return global::System.Threading.Tasks.Task.CompletedTask;
        }

        state.ActiveExecutions = Math.Max(0, state.ActiveExecutions - 1);
        state.LastHeartbeatUtc = DateTimeOffset.UtcNow;

        if (result.IsSuccess)
        {
            state.ConsecutiveFailures = 0;
            if (state.Health == "restarted")
            {
                state.Health = "healthy";
            }

            return global::System.Threading.Tasks.Task.CompletedTask;
        }

        bool unsafeFailure = result.Errors.Any(error =>
            error.Contains(_options.UnsafeErrorKeyword, StringComparison.OrdinalIgnoreCase));

        if (unsafeFailure)
        {
            state.Health = "terminated";
            _logger.LogWarning("Supervisor terminated agent {AgentId} because unsafe execution was detected.", agent.Id);
            return global::System.Threading.Tasks.Task.CompletedTask;
        }

        state.ConsecutiveFailures++;
        if (state.ConsecutiveFailures < Math.Max(1, _options.ConsecutiveFailuresBeforeRestart))
        {
            return global::System.Threading.Tasks.Task.CompletedTask;
        }

        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddHours(-1);
        while (state.RestartTimestamps.TryPeek(out DateTimeOffset restartAt) && restartAt < cutoff)
        {
            state.RestartTimestamps.TryDequeue(out _);
        }

        if (state.RestartTimestamps.Count >= Math.Max(1, _options.MaxRestartsPerHour))
        {
            state.Health = "terminated";
            _logger.LogWarning("Supervisor terminated agent {AgentId} after restart quota was exceeded.", agent.Id);
            return global::System.Threading.Tasks.Task.CompletedTask;
        }

        state.RestartTimestamps.Enqueue(DateTimeOffset.UtcNow);
        state.ConsecutiveFailures = 0;
        state.Health = "restarted";
        _logger.LogInformation("Supervisor restarted agent {AgentId} after repeated failures.", agent.Id);

        return global::System.Threading.Tasks.Task.CompletedTask;
    }
}
