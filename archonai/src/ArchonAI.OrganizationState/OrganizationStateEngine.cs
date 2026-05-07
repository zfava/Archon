using System.Collections.Concurrent;
using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Knowledge;
using ArchonAI.Core.Models.OrganizationState;
using ArchonAI.Core.Models.Perception;
using Microsoft.Extensions.Logging;

namespace ArchonAI.OrganizationState;

public sealed class OrganizationStateEngine : IOrganizationStateEngine
{
    private readonly IKnowledgeGraphStore _knowledgeGraph;
    private readonly IMemoryStore _memoryStore;
    private readonly ILogger<OrganizationStateEngine> _logger;

    private readonly ConcurrentDictionary<string, DepartmentState> _departments = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ResourceState> _resources = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CustomerState> _customers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _globalMetrics = new(StringComparer.OrdinalIgnoreCase);

    private long _version;
    private long _totalUpdates;
    private DateTimeOffset _lastUpdateUtc = DateTimeOffset.UtcNow;

    private const string MemoryScope = "organization-state";
    private const string KgNodeType = "org-state";

    public OrganizationStateEngine(
        IKnowledgeGraphStore knowledgeGraph,
        IMemoryStore memoryStore,
        ILogger<OrganizationStateEngine> logger)
    {
        _knowledgeGraph = knowledgeGraph;
        _memoryStore = memoryStore;
        _logger = logger;

        SeedDefaults();
    }

    public async global::System.Threading.Tasks.Task<StateUpdateResult> ApplyObservationAsync(
        OperationalObservation observation,
        CancellationToken cancellationToken = default)
    {
        long newVersion = Interlocked.Increment(ref _version);
        Interlocked.Increment(ref _totalUpdates);

        int fieldsUpdated = 0;

        fieldsUpdated += ApplyToDepartment(observation);
        fieldsUpdated += ApplyToResources(observation);
        fieldsUpdated += ApplyToCustomers(observation);
        fieldsUpdated += ApplyToGlobalMetrics(observation);

        _lastUpdateUtc = DateTimeOffset.UtcNow;

        await PersistToKnowledgeGraphAsync(observation, cancellationToken);
        await PersistToMemoryAsync(observation, newVersion, cancellationToken);

        _logger.LogDebug(
            "State v{Version}: applied observation {ObservationId} [{Category}], {Fields} fields updated",
            newVersion, observation.ObservationId, observation.Category, fieldsUpdated);

        return new StateUpdateResult(
            SnapshotId: Guid.NewGuid(),
            NewVersion: newVersion,
            FieldsUpdated: fieldsUpdated,
            UpdatedAtUtc: _lastUpdateUtc);
    }

    public global::System.Threading.Tasks.Task<OperationalState> GetCurrentStateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return global::System.Threading.Tasks.Task.FromResult(BuildCurrentState());
    }

    public global::System.Threading.Tasks.Task<DepartmentState?> GetDepartmentStateAsync(
        string departmentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _departments.TryGetValue(departmentId, out var dept);
        return global::System.Threading.Tasks.Task.FromResult(dept);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<ResourceState>> GetResourcesByDepartmentAsync(
        string departmentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ResourceState> result = _resources.Values
            .Where(r => r.DepartmentId.Equals(departmentId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<CustomerState?> GetCustomerStateAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _customers.TryGetValue(customerId, out var cust);
        return global::System.Threading.Tasks.Task.FromResult(cust);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<CustomerState>> GetCustomersByHealthAsync(
        CustomerHealthStatus status,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<CustomerState> result = _customers.Values
            .Where(c => c.HealthStatus == status)
            .OrderByDescending(c => c.LifetimeValue)
            .ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public async global::System.Threading.Tasks.Task<OperationalState> TakeSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var state = BuildCurrentState();

        var snapshotJson = JsonSerializer.Serialize(new
        {
            state.SnapshotId,
            state.Version,
            DepartmentCount = state.Departments.Count,
            ResourceCount = state.Resources.Count,
            CustomerCount = state.Customers.Count,
            state.CapturedAtUtc
        });

        var memoryRecord = new MemoryRecord(
            Id: Guid.NewGuid(),
            MemoryType: "state-snapshot",
            Scope: MemoryScope,
            Content: snapshotJson,
            Metadata: new Dictionary<string, string>
            {
                ["version"] = state.Version.ToString(),
                ["snapshotId"] = state.SnapshotId.ToString(),
                ["departments"] = state.Departments.Count.ToString(),
                ["resources"] = state.Resources.Count.ToString(),
                ["customers"] = state.Customers.Count.ToString()
            },
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: null);

        await _memoryStore.SaveAsync(memoryRecord, cancellationToken);

        var kgNode = new KnowledgeNode(
            NodeId: $"snapshot:{state.SnapshotId}",
            NodeType: "state-snapshot",
            DisplayName: $"State Snapshot v{state.Version}",
            Properties: new Dictionary<string, string>
            {
                ["version"] = state.Version.ToString(),
                ["capturedAt"] = state.CapturedAtUtc.ToString("O")
            },
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        await _knowledgeGraph.UpsertNodeAsync(kgNode, cancellationToken);

        _logger.LogInformation("State snapshot v{Version} persisted to KnowledgeGraph and Memory", state.Version);
        return state;
    }

    public global::System.Threading.Tasks.Task<OrganizationStateDashboard> GetDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var deptHealthScores = _departments.Values
            .GroupBy(d => d.Type)
            .ToDictionary(g => g.Key, g => g.Average(d => d.HealthScore));

        var resourceUtil = _resources.Values
            .GroupBy(r => r.Kind)
            .ToDictionary(g => g.Key, g =>
            {
                double totalAllocated = g.Sum(r => r.Allocated);
                double totalUtilized = g.Sum(r => r.Utilized);
                return totalAllocated > 0 ? totalUtilized / totalAllocated * 100.0 : 0.0;
            });

        var customerDist = _customers.Values
            .GroupBy(c => c.HealthStatus)
            .ToDictionary(g => g.Key, g => g.Count());

        double overallHealth = deptHealthScores.Count > 0
            ? deptHealthScores.Values.Average()
            : 1.0;

        var dashboard = new OrganizationStateDashboard(
            CurrentVersion: Interlocked.Read(ref _version),
            TotalDepartments: _departments.Count,
            TotalResources: _resources.Count,
            TotalCustomers: _customers.Count,
            OverallHealthScore: Math.Round(overallHealth, 3),
            DepartmentHealthScores: deptHealthScores,
            ResourceUtilizationPercent: resourceUtil,
            CustomerDistribution: customerDist,
            TotalStateUpdates: Interlocked.Read(ref _totalUpdates),
            LastUpdateUtc: _lastUpdateUtc,
            GeneratedAtUtc: DateTimeOffset.UtcNow);

        return global::System.Threading.Tasks.Task.FromResult(dashboard);
    }

    // ══════════════════════════════════════════════════════════════
    //  Signal → State Mutation
    // ══════════════════════════════════════════════════════════════

    private int ApplyToDepartment(OperationalObservation observation)
    {
        var deptId = MapSourceToDepartment(observation.SourceSystem);
        int updated = 0;

        _departments.AddOrUpdate(deptId,
            _ =>
            {
                updated = 1;
                return new DepartmentState(
                    DepartmentId: deptId,
                    Type: MapSourceToDepartmentType(observation.SourceSystem),
                    DisplayName: deptId,
                    ActiveWorkflows: 0,
                    PendingTasks: 1,
                    CompletedTasks: 0,
                    HealthScore: 1.0,
                    Metrics: new Dictionary<string, string>
                    {
                        ["lastObservationCategory"] = observation.Category.ToString()
                    },
                    LastUpdatedUtc: DateTimeOffset.UtcNow);
            },
            (_, existing) =>
            {
                updated = 1;
                var metrics = new Dictionary<string, string>(existing.Metrics, StringComparer.OrdinalIgnoreCase)
                {
                    ["lastObservationCategory"] = observation.Category.ToString(),
                    ["lastObservationSeverity"] = observation.Severity.ToString()
                };

                double healthDelta = observation.Severity switch
                {
                    ObservationSeverity.Critical => -0.15,
                    ObservationSeverity.High => -0.08,
                    ObservationSeverity.Medium => -0.02,
                    _ => 0.01
                };

                double newHealth = Math.Clamp(existing.HealthScore + healthDelta, 0.0, 1.0);

                return existing with
                {
                    PendingTasks = existing.PendingTasks + 1,
                    HealthScore = Math.Round(newHealth, 3),
                    Metrics = metrics,
                    LastUpdatedUtc = DateTimeOffset.UtcNow
                };
            });

        return updated;
    }

    private int ApplyToResources(OperationalObservation observation)
    {
        if (observation.Category is not (ObservationCategory.Revenue or ObservationCategory.Cost
            or ObservationCategory.InventoryMovement))
        {
            return 0;
        }

        var deptId = MapSourceToDepartment(observation.SourceSystem);
        var kind = observation.Category switch
        {
            ObservationCategory.Revenue => ResourceKind.Budget,
            ObservationCategory.Cost => ResourceKind.Budget,
            ObservationCategory.InventoryMovement => ResourceKind.Inventory,
            _ => ResourceKind.Capacity
        };

        var resourceId = $"{deptId}:{kind}";

        double amount = 0;
        if (observation.StructuredData.TryGetValue("payload.amount", out var amountStr))
        {
            double.TryParse(amountStr, out amount);
        }

        _resources.AddOrUpdate(resourceId,
            _ => new ResourceState(
                ResourceId: resourceId,
                Kind: kind,
                DepartmentId: deptId,
                Allocated: 1_000_000,
                Utilized: Math.Abs(amount),
                Available: 1_000_000 - Math.Abs(amount),
                Unit: kind == ResourceKind.Inventory ? "units" : "USD",
                Metadata: new Dictionary<string, string>
                {
                    ["lastSignalEntity"] = observation.EntityId
                },
                LastUpdatedUtc: DateTimeOffset.UtcNow),
            (_, existing) =>
            {
                double newUtilized = existing.Utilized + Math.Abs(amount);
                return existing with
                {
                    Utilized = newUtilized,
                    Available = Math.Max(0, existing.Allocated - newUtilized),
                    Metadata = new Dictionary<string, string>(existing.Metadata, StringComparer.OrdinalIgnoreCase)
                    {
                        ["lastSignalEntity"] = observation.EntityId
                    },
                    LastUpdatedUtc = DateTimeOffset.UtcNow
                };
            });

        return 1;
    }

    private int ApplyToCustomers(OperationalObservation observation)
    {
        if (observation.Category is not (ObservationCategory.CustomerActivity
            or ObservationCategory.Revenue or ObservationCategory.OrderLifecycle))
        {
            return 0;
        }

        var customerId = observation.EntityId;

        _customers.AddOrUpdate(customerId,
            _ => new CustomerState(
                CustomerId: customerId,
                Name: customerId,
                HealthStatus: CustomerHealthStatus.New,
                LifetimeValue: 0,
                OpenTickets: 0,
                ActiveDeals: observation.Category == ObservationCategory.OrderLifecycle ? 1 : 0,
                LastInteractionUtc: observation.ObservedAtUtc,
                Attributes: new Dictionary<string, string>
                {
                    ["source"] = observation.SourceSystem.ToString()
                },
                LastUpdatedUtc: DateTimeOffset.UtcNow),
            (_, existing) =>
            {
                double ltv = existing.LifetimeValue;
                if (observation.StructuredData.TryGetValue("payload.amount", out var amountStr)
                    && double.TryParse(amountStr, out var amount))
                {
                    ltv += amount;
                }

                var health = existing.HealthStatus;
                if (observation.Severity >= ObservationSeverity.High)
                    health = CustomerHealthStatus.AtRisk;
                else if (ltv > existing.LifetimeValue && health == CustomerHealthStatus.Healthy)
                    health = CustomerHealthStatus.Expanding;

                return existing with
                {
                    LifetimeValue = ltv,
                    HealthStatus = health,
                    ActiveDeals = observation.Category == ObservationCategory.OrderLifecycle
                        ? existing.ActiveDeals + 1
                        : existing.ActiveDeals,
                    LastInteractionUtc = observation.ObservedAtUtc,
                    LastUpdatedUtc = DateTimeOffset.UtcNow
                };
            });

        return 1;
    }

    private int ApplyToGlobalMetrics(OperationalObservation observation)
    {
        _globalMetrics[$"lastCategory"] = observation.Category.ToString();
        _globalMetrics[$"lastSource"] = observation.SourceSystem.ToString();
        _globalMetrics[$"lastSeverity"] = observation.Severity.ToString();
        _globalMetrics[$"totalObservationsApplied"] =
            (Interlocked.Read(ref _totalUpdates)).ToString();
        return 1;
    }

    // ══════════════════════════════════════════════════════════════
    //  Persistence
    // ══════════════════════════════════════════════════════════════

    private async global::System.Threading.Tasks.Task PersistToKnowledgeGraphAsync(
        OperationalObservation observation,
        CancellationToken cancellationToken)
    {
        try
        {
            var deptId = MapSourceToDepartment(observation.SourceSystem);

            var deptNode = new KnowledgeNode(
                NodeId: $"dept:{deptId}",
                NodeType: KgNodeType,
                DisplayName: deptId,
                Properties: _departments.TryGetValue(deptId, out var dept)
                    ? new Dictionary<string, string>
                    {
                        ["healthScore"] = dept.HealthScore.ToString("F3"),
                        ["pendingTasks"] = dept.PendingTasks.ToString(),
                        ["type"] = dept.Type.ToString()
                    }
                    : new Dictionary<string, string>(),
                UpdatedAtUtc: DateTimeOffset.UtcNow);

            await _knowledgeGraph.UpsertNodeAsync(deptNode, cancellationToken);

            var relationship = new KnowledgeRelationship(
                RelationshipId: $"obs:{observation.ObservationId}→dept:{deptId}",
                FromNodeId: $"observation:{observation.ObservationId}",
                RelationshipType: "updated-state",
                ToNodeId: $"dept:{deptId}",
                Properties: new Dictionary<string, string>
                {
                    ["category"] = observation.Category.ToString(),
                    ["severity"] = observation.Severity.ToString()
                },
                UpdatedAtUtc: DateTimeOffset.UtcNow);

            await _knowledgeGraph.UpsertRelationshipAsync(relationship, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist state update to KnowledgeGraph");
        }
    }

    private async global::System.Threading.Tasks.Task PersistToMemoryAsync(
        OperationalObservation observation,
        long version,
        CancellationToken cancellationToken)
    {
        try
        {
            var record = new MemoryRecord(
                Id: Guid.NewGuid(),
                MemoryType: "state-update",
                Scope: MemoryScope,
                Content: $"[v{version}] {observation.Summary}",
                Metadata: new Dictionary<string, string>
                {
                    ["version"] = version.ToString(),
                    ["observationId"] = observation.ObservationId.ToString(),
                    ["category"] = observation.Category.ToString(),
                    ["severity"] = observation.Severity.ToString(),
                    ["sourceSystem"] = observation.SourceSystem.ToString(),
                    ["entityId"] = observation.EntityId
                },
                CreatedAtUtc: DateTimeOffset.UtcNow,
                ExpiresAtUtc: null);

            await _memoryStore.SaveAsync(record, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist state update to Memory");
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private OperationalState BuildCurrentState()
    {
        return new OperationalState(
            SnapshotId: Guid.NewGuid(),
            Version: Interlocked.Read(ref _version),
            Departments: _departments.Values.OrderBy(d => d.DepartmentId).ToList(),
            Resources: _resources.Values.OrderBy(r => r.ResourceId).ToList(),
            Customers: _customers.Values.OrderBy(c => c.CustomerId).ToList(),
            GlobalMetrics: new Dictionary<string, string>(_globalMetrics),
            CapturedAtUtc: DateTimeOffset.UtcNow);
    }

    private static string MapSourceToDepartment(SourceSystem source) => source switch
    {
        SourceSystem.CRM => "sales",
        SourceSystem.ERP => "operations",
        SourceSystem.Finance => "finance",
        SourceSystem.Marketing => "marketing",
        SourceSystem.Logistics => "logistics",
        _ => "general"
    };

    private static DepartmentType MapSourceToDepartmentType(SourceSystem source) => source switch
    {
        SourceSystem.CRM => DepartmentType.Sales,
        SourceSystem.ERP => DepartmentType.Operations,
        SourceSystem.Finance => DepartmentType.Finance,
        SourceSystem.Marketing => DepartmentType.Marketing,
        SourceSystem.Logistics => DepartmentType.Logistics,
        _ => DepartmentType.Operations
    };

    private void SeedDefaults()
    {
        var defaultDepts = new[]
        {
            (DepartmentType.Sales, "sales"),
            (DepartmentType.Finance, "finance"),
            (DepartmentType.Marketing, "marketing"),
            (DepartmentType.Operations, "operations"),
            (DepartmentType.Logistics, "logistics")
        };

        foreach (var (type, id) in defaultDepts)
        {
            _departments.TryAdd(id, new DepartmentState(
                DepartmentId: id,
                Type: type,
                DisplayName: type.ToString(),
                ActiveWorkflows: 0,
                PendingTasks: 0,
                CompletedTasks: 0,
                HealthScore: 1.0,
                Metrics: new Dictionary<string, string>(),
                LastUpdatedUtc: DateTimeOffset.UtcNow));
        }
    }
}
