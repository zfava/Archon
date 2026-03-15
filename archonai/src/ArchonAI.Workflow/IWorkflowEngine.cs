namespace ArchonAI.Workflow;

public interface IWorkflowEngine
{
    WorkflowState Initialize(Guid workflowId);

    WorkflowState GetState(Guid workflowId);

    WorkflowState Transition(Guid workflowId, WorkflowTrigger trigger);
}
