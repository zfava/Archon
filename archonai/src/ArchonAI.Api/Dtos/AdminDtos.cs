namespace ArchonAI.Api.Dtos;

public sealed record AgentEnabledRequest(bool Enabled);
public sealed record CreateRoleRequest(string Name, string Description, IReadOnlyList<string> Permissions);
public sealed record UpdateRoleRequest(string Description, IReadOnlyList<string> Permissions);
public sealed record AssignRoleRequest(string SubjectId, string SubjectType, Guid RoleId, string AssignedBy);
public sealed record CreatePolicyRequest(string Name, string Description, IReadOnlyList<string> RequiredPermissions, string Resource, string Effect, Dictionary<string, string> Conditions);
public sealed record UpdatePolicyEnabledRequest(bool IsEnabled);
public sealed record EvaluateAccessRequest(string SubjectId, string Resource, string Action);
public sealed record AuditRecordRequest(string EventType, string Category, string Source, string SubjectId, string SubjectType, string Action, string ResourceType, string ResourceId, string Description, Dictionary<string, string>? Metadata = null);
public sealed record AuditVerifyRequest(Guid? FromEntryId = null);
public sealed record AddSecurityPolicyRequest(string Name, string Category, string RuleType, IReadOnlyList<string>? AllowedValues = null, IReadOnlyList<string>? DeniedValues = null, Dictionary<string, string>? Limits = null);
public sealed record EvaluateDataAccessRequest(string SubjectId, string ResourceType, string Action);
public sealed record EvaluateWorkflowLimitsRequest(Guid WorkflowId, int StepCount, int ConcurrentAgents);
public sealed record DataErasureRequest(string Justification);
