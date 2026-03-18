using ArchonAI.Workflow;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Integration tests for the deterministic workflow state machine.
/// Validates: all valid transitions, invalid transition rejection,
/// concurrent access safety, and lifecycle completeness.
/// </summary>
public sealed class WorkflowStateMachineTests
{
    private readonly WorkflowEngine _engine = new();

    // ── Happy Path Lifecycle ──────────────────────────────────────────

    [Fact]
    public void FullLifecycle_Created_Through_Completed()
    {
        var id = Guid.NewGuid();
        Assert.Equal(WorkflowState.Created, _engine.Initialize(id));
        Assert.Equal(WorkflowState.Planning, _engine.Transition(id, WorkflowTrigger.StartPlanning));
        Assert.Equal(WorkflowState.Scheduled, _engine.Transition(id, WorkflowTrigger.Schedule));
        Assert.Equal(WorkflowState.Executing, _engine.Transition(id, WorkflowTrigger.StartExecuting));
        Assert.Equal(WorkflowState.Evaluating, _engine.Transition(id, WorkflowTrigger.StartEvaluating));
        Assert.Equal(WorkflowState.Completed, _engine.Transition(id, WorkflowTrigger.Complete));
    }

    [Fact]
    public void FullLifecycle_Through_Failed_And_Escalated()
    {
        var id = Guid.NewGuid();
        _engine.Initialize(id);
        _engine.Transition(id, WorkflowTrigger.StartPlanning);
        _engine.Transition(id, WorkflowTrigger.Schedule);
        _engine.Transition(id, WorkflowTrigger.StartExecuting);
        _engine.Transition(id, WorkflowTrigger.StartEvaluating);
        Assert.Equal(WorkflowState.Failed, _engine.Transition(id, WorkflowTrigger.Fail));
        Assert.Equal(WorkflowState.Escalated, _engine.Transition(id, WorkflowTrigger.Escalate));
    }

    // ── Human Intervention Transitions ────────────────────────────────

    [Fact]
    public void PauseFromExecuting_And_Resume()
    {
        var id = Guid.NewGuid();
        _engine.Initialize(id);
        _engine.Transition(id, WorkflowTrigger.StartPlanning);
        _engine.Transition(id, WorkflowTrigger.Schedule);
        _engine.Transition(id, WorkflowTrigger.StartExecuting);

        Assert.Equal(WorkflowState.Paused, _engine.Transition(id, WorkflowTrigger.Pause));
        Assert.Equal(WorkflowState.Executing, _engine.Transition(id, WorkflowTrigger.Resume));
    }

    [Fact]
    public void PauseFromScheduled_Works()
    {
        var id = Guid.NewGuid();
        _engine.Initialize(id);
        _engine.Transition(id, WorkflowTrigger.StartPlanning);
        _engine.Transition(id, WorkflowTrigger.Schedule);

        Assert.Equal(WorkflowState.Paused, _engine.Transition(id, WorkflowTrigger.Pause));
    }

    [Fact]
    public void CancelFromPaused_Works()
    {
        var id = Guid.NewGuid();
        _engine.Initialize(id);
        _engine.Transition(id, WorkflowTrigger.StartPlanning);
        _engine.Transition(id, WorkflowTrigger.Schedule);
        _engine.Transition(id, WorkflowTrigger.Pause);

        Assert.Equal(WorkflowState.Cancelled, _engine.Transition(id, WorkflowTrigger.Cancel));
    }

    // ── Cancel from Multiple Valid States ──────────────────────────────

    [Theory]
    [InlineData(WorkflowTrigger.StartPlanning)] // → Planning
    public void CancelFromPlanning_Works(WorkflowTrigger setupTrigger)
    {
        var id = Guid.NewGuid();
        _engine.Initialize(id);
        _engine.Transition(id, setupTrigger);

        Assert.Equal(WorkflowState.Cancelled, _engine.Transition(id, WorkflowTrigger.Cancel));
    }

    [Fact]
    public void CancelFromScheduled_Works()
    {
        var id = Guid.NewGuid();
        _engine.Initialize(id);
        _engine.Transition(id, WorkflowTrigger.StartPlanning);
        _engine.Transition(id, WorkflowTrigger.Schedule);

        Assert.Equal(WorkflowState.Cancelled, _engine.Transition(id, WorkflowTrigger.Cancel));
    }

    [Fact]
    public void CancelFromExecuting_Works()
    {
        var id = Guid.NewGuid();
        _engine.Initialize(id);
        _engine.Transition(id, WorkflowTrigger.StartPlanning);
        _engine.Transition(id, WorkflowTrigger.Schedule);
        _engine.Transition(id, WorkflowTrigger.StartExecuting);

        Assert.Equal(WorkflowState.Cancelled, _engine.Transition(id, WorkflowTrigger.Cancel));
    }

    // ── Invalid Transition Rejection ──────────────────────────────────

    [Fact]
    public void InvalidTransition_Created_Execute_Throws()
    {
        var id = Guid.NewGuid();
        _engine.Initialize(id);

        Assert.Throws<InvalidOperationException>(() =>
            _engine.Transition(id, WorkflowTrigger.StartExecuting));
    }

    [Fact]
    public void InvalidTransition_Completed_Resume_Throws()
    {
        var id = Guid.NewGuid();
        _engine.Initialize(id);
        _engine.Transition(id, WorkflowTrigger.StartPlanning);
        _engine.Transition(id, WorkflowTrigger.Schedule);
        _engine.Transition(id, WorkflowTrigger.StartExecuting);
        _engine.Transition(id, WorkflowTrigger.StartEvaluating);
        _engine.Transition(id, WorkflowTrigger.Complete);

        Assert.Throws<InvalidOperationException>(() =>
            _engine.Transition(id, WorkflowTrigger.Resume));
    }

    [Fact]
    public void InvalidTransition_Cancelled_StartPlanning_Throws()
    {
        var id = Guid.NewGuid();
        _engine.Initialize(id);
        _engine.Transition(id, WorkflowTrigger.StartPlanning);
        _engine.Transition(id, WorkflowTrigger.Cancel);

        Assert.Throws<InvalidOperationException>(() =>
            _engine.Transition(id, WorkflowTrigger.StartPlanning));
    }

    [Fact]
    public void InvalidTransition_Created_Cancel_Throws()
    {
        var id = Guid.NewGuid();
        _engine.Initialize(id);

        // Cannot cancel from Created state (not in transition table)
        Assert.Throws<InvalidOperationException>(() =>
            _engine.Transition(id, WorkflowTrigger.Cancel));
    }

    // ── GetState for Unknown Workflow ──────────────────────────────────

    [Fact]
    public void GetState_UnknownId_ReturnsCreated()
    {
        var id = Guid.NewGuid();
        Assert.Equal(WorkflowState.Created, _engine.GetState(id));
    }

    // ── Concurrent Access Safety ──────────────────────────────────────

    [Fact]
    public void ConcurrentInitialize_SameId_IsIdempotent()
    {
        var id = Guid.NewGuid();
        var results = Enumerable.Range(0, 100)
            .AsParallel()
            .Select(_ => _engine.Initialize(id))
            .ToList();

        Assert.All(results, s => Assert.Equal(WorkflowState.Created, s));
    }

    // ── State Isolation Between Workflows ─────────────────────────────

    [Fact]
    public void DifferentWorkflows_HaveIndependentState()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        _engine.Initialize(id1);
        _engine.Initialize(id2);

        _engine.Transition(id1, WorkflowTrigger.StartPlanning);

        Assert.Equal(WorkflowState.Planning, _engine.GetState(id1));
        Assert.Equal(WorkflowState.Created, _engine.GetState(id2));
    }
}
