using ArchonAI.Core.Models.ControlPlane;
using ArchonAI.Core.Models.Workflow;

namespace ArchonAI.Api.Dtos;

public sealed record WorkflowCoordinationRequest(string WorkflowTemplate, IReadOnlyList<string> AgentCapabilities, Dictionary<string, string> Inputs);
public sealed record CreateWorkflowStepRequest(int Order, string Name, string Description, string AgentType, Dictionary<string, string> Inputs);
public sealed record CreateWorkflowRequest(string Name, string Description, string Strategy, IReadOnlyList<CreateWorkflowStepRequest> Steps, Dictionary<string, string>? Metadata = null);
public sealed record ExecuteWorkflowRequest(string TenantId, Dictionary<string, string>? Metadata = null);
public sealed record CreateWorkflowNodeRequest(string Name, string Description, WorkflowNodeType NodeType, Dictionary<string, string> Configuration);
public sealed record CreateWorkflowEdgeRequest(int SourceNodeIndex, int TargetNodeIndex, string? Label = null);
public sealed record CreateWorkflowGraphRequest(string Name, string Description, IReadOnlyList<CreateWorkflowNodeRequest> Nodes, IReadOnlyList<CreateWorkflowEdgeRequest> Edges, Dictionary<string, string>? Metadata = null);
public sealed record SimulateWorkflowRequest(Guid WorkflowGraphId, Guid? StrategyId = null, Dictionary<string, string>? HistoricalOverrides = null);
public sealed record RecordHistoricalExecutionRequest(Guid WorkflowGraphId, bool IsSuccess, double LatencyMs, double Cost);
public sealed record ProvisionTenantRequest(string Name, string DisplayName, TenantTier Tier, Dictionary<string, string>? Metadata = null);
public sealed record SuspendTenantRequest(string Reason);
public sealed record RegisterWorkflowRequest(string TenantId, string Name, string Description, string Strategy, int StepCount, Dictionary<string, string>? Metadata = null);
public sealed record UpdateManagedStatusRequest(ManagedWorkflowStatus Status);
public sealed record RegisterManagedAgentRequest(string TenantId, string Name, string Version, IReadOnlyList<string> Capabilities, Dictionary<string, string>? Configuration = null);
public sealed record UpdateManagedAgentStatusRequest(ManagedAgentStatus Status);
public sealed record CreatePlatformPolicyRequest(string TenantId, string Name, string Description, PlatformPolicyType PolicyType, string TargetResource, Dictionary<string, string> Rules, int Priority);
public sealed record UpdatePlatformPolicyRequest(bool IsEnabled, Dictionary<string, string>? Rules = null, int? Priority = null);
public sealed record SetConfigurationRequest(string TenantId, string Scope, string Key, string Value, string? Description = null, bool IsSecret = false);
