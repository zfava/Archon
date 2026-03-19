namespace ArchonAI.Policy;

public sealed class PolicyOptions
{
    public int MaxTaskInputCount { get; set; } = 50;
    public List<string> ForbiddenCapabilities { get; set; } = new();
    public List<string> HighRiskCapabilities { get; set; } = new() { "financial-operations", "external.connector", "workflow-orchestration" };
    public List<string> ApprovalCheckpointCapabilities { get; set; } = new();
    public string DefaultApprovalCheckpoint { get; set; } = "operator-review";
    public bool RequireApprovalCheckpointForHighRisk { get; set; } = true;
    /// <summary>
    /// Default confidence score used when no confidence signal is present in context or task.
    /// Set this lower than MinConfidenceThreshold to force policy review on unknown tasks.
    /// Default: 0.5 (neutral — triggers review at standard thresholds)
    /// </summary>
    public double DefaultConfidenceScore { get; set; } = 0.5;

    public double MinConfidenceThreshold { get; set; } = 0.65;
    public double ConfidenceRiskWeight { get; set; } = 50;
    public double AutoBlockRiskThreshold { get; set; } = 80;
    public double ApprovalRiskThreshold { get; set; } = 60;
    public string ManualOverrideSigningKey { get; set; } = string.Empty;
}
