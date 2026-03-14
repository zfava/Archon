namespace ArchonAI.Policy;

public sealed class PolicyOptions
{
    public int MaxTaskInputCount { get; set; } = 50;
    public List<string> ForbiddenCapabilities { get; set; } = new();
    public List<string> HighRiskCapabilities { get; set; } = new() { "financial-operations", "external.connector", "workflow-orchestration" };
    public List<string> ApprovalCheckpointCapabilities { get; set; } = new();
    public string DefaultApprovalCheckpoint { get; set; } = "operator-review";
    public bool RequireApprovalCheckpointForHighRisk { get; set; } = true;
    public double MinConfidenceThreshold { get; set; } = 0.65;
    public double ConfidenceRiskWeight { get; set; } = 50;
    public double AutoBlockRiskThreshold { get; set; } = 80;
    public double ApprovalRiskThreshold { get; set; } = 60;
}
