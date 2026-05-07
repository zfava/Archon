using System.Collections.Concurrent;

namespace ArchonAI.Workflow;

/// <summary>
/// Deterministic state machine for workflow lifecycle transitions.
/// </summary>
public sealed class WorkflowEngine : IWorkflowEngine
{
    private static readonly IReadOnlyDictionary<(WorkflowState State, WorkflowTrigger Trigger), WorkflowState> TransitionTable =
        new Dictionary<(WorkflowState, WorkflowTrigger), WorkflowState>
        {
            [(WorkflowState.Created, WorkflowTrigger.StartPlanning)] = WorkflowState.Planning,
            [(WorkflowState.Planning, WorkflowTrigger.Schedule)] = WorkflowState.Scheduled,
            [(WorkflowState.Scheduled, WorkflowTrigger.StartExecuting)] = WorkflowState.Executing,
            [(WorkflowState.Executing, WorkflowTrigger.StartEvaluating)] = WorkflowState.Evaluating,
            [(WorkflowState.Evaluating, WorkflowTrigger.Complete)] = WorkflowState.Completed,
            [(WorkflowState.Evaluating, WorkflowTrigger.Fail)] = WorkflowState.Failed,
            [(WorkflowState.Failed, WorkflowTrigger.Escalate)] = WorkflowState.Escalated,
            // Human intervention transitions
            [(WorkflowState.Executing, WorkflowTrigger.Pause)] = WorkflowState.Paused,
            [(WorkflowState.Scheduled, WorkflowTrigger.Pause)] = WorkflowState.Paused,
            [(WorkflowState.Paused, WorkflowTrigger.Resume)] = WorkflowState.Executing,
            [(WorkflowState.Executing, WorkflowTrigger.Cancel)] = WorkflowState.Cancelled,
            [(WorkflowState.Scheduled, WorkflowTrigger.Cancel)] = WorkflowState.Cancelled,
            [(WorkflowState.Paused, WorkflowTrigger.Cancel)] = WorkflowState.Cancelled,
            [(WorkflowState.Planning, WorkflowTrigger.Cancel)] = WorkflowState.Cancelled
        };

    private readonly ConcurrentDictionary<Guid, WorkflowState> _states = new();

    public WorkflowState Initialize(Guid workflowId)
        => _states.GetOrAdd(workflowId, WorkflowState.Created);

    public WorkflowState GetState(Guid workflowId)
        => _states.TryGetValue(workflowId, out WorkflowState state)
            ? state
            : Initialize(workflowId);

    public WorkflowState Transition(Guid workflowId, WorkflowTrigger trigger)
    {
        WorkflowState initial = Initialize(workflowId);

        return _states.AddOrUpdate(workflowId,
            _ => ResolveNextState(initial, trigger),
            (_, current) => ResolveNextState(current, trigger));
    }

    private static WorkflowState ResolveNextState(WorkflowState currentState, WorkflowTrigger trigger)
    {
        if (TransitionTable.TryGetValue((currentState, trigger), out WorkflowState nextState))
        {
            return nextState;
        }

        throw new InvalidOperationException(
            $"Invalid workflow transition. CurrentState={currentState}, Trigger={trigger}.");
    }
}
