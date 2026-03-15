namespace ArchonAI.Workflow;

public enum WorkflowTrigger
{
    StartPlanning = 0,
    Schedule = 1,
    StartExecuting = 2,
    StartEvaluating = 3,
    Complete = 4,
    Fail = 5,
    Escalate = 6
}
