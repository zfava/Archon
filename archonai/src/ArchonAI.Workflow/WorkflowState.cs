namespace ArchonAI.Workflow;

public enum WorkflowState
{
    Created = 0,
    Planning = 1,
    Scheduled = 2,
    Executing = 3,
    Evaluating = 4,
    Completed = 5,
    Failed = 6,
    Escalated = 7,
    Paused = 8,
    Cancelled = 9
}
