using ArchonAI.Core.Models.Perception;

namespace ArchonAI.Core.Models.OrganizationState;

public enum DepartmentType
{
    Sales,
    Finance,
    Marketing,
    Operations,
    Support,
    Logistics,
    Engineering,
    HumanResources
}

public enum ResourceKind
{
    Budget,
    Headcount,
    Infrastructure,
    Inventory,
    Capacity
}

public enum CustomerHealthStatus
{
    Healthy,
    AtRisk,
    Churning,
    New,
    Expanding
}

public sealed record OperationalState(
    Guid SnapshotId,
    long Version,
    IReadOnlyList<DepartmentState> Departments,
    IReadOnlyList<ResourceState> Resources,
    IReadOnlyList<CustomerState> Customers,
    IReadOnlyDictionary<string, string> GlobalMetrics,
    DateTimeOffset CapturedAtUtc);

public sealed record DepartmentState(
    string DepartmentId,
    DepartmentType Type,
    string DisplayName,
    int ActiveWorkflows,
    int PendingTasks,
    int CompletedTasks,
    double HealthScore,
    IReadOnlyDictionary<string, string> Metrics,
    DateTimeOffset LastUpdatedUtc);

public sealed record ResourceState(
    string ResourceId,
    ResourceKind Kind,
    string DepartmentId,
    double Allocated,
    double Utilized,
    double Available,
    string Unit,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset LastUpdatedUtc);

public sealed record CustomerState(
    string CustomerId,
    string Name,
    CustomerHealthStatus HealthStatus,
    double LifetimeValue,
    int OpenTickets,
    int ActiveDeals,
    DateTimeOffset LastInteractionUtc,
    IReadOnlyDictionary<string, string> Attributes,
    DateTimeOffset LastUpdatedUtc);

public sealed record StateUpdateResult(
    Guid SnapshotId,
    long NewVersion,
    int FieldsUpdated,
    DateTimeOffset UpdatedAtUtc);

public sealed record OrganizationStateDashboard(
    long CurrentVersion,
    int TotalDepartments,
    int TotalResources,
    int TotalCustomers,
    double OverallHealthScore,
    IReadOnlyDictionary<DepartmentType, double> DepartmentHealthScores,
    IReadOnlyDictionary<ResourceKind, double> ResourceUtilizationPercent,
    IReadOnlyDictionary<CustomerHealthStatus, int> CustomerDistribution,
    long TotalStateUpdates,
    DateTimeOffset LastUpdateUtc,
    DateTimeOffset GeneratedAtUtc);
