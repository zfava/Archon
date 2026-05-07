using ArchonAI.Core.Models.OrganizationState;
using ArchonAI.Core.Models.Perception;

namespace ArchonAI.Core.Interfaces;

public interface IOrganizationStateEngine
{
    global::System.Threading.Tasks.Task<StateUpdateResult> ApplyObservationAsync(
        OperationalObservation observation,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<OperationalState> GetCurrentStateAsync(
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<DepartmentState?> GetDepartmentStateAsync(
        string departmentId,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<ResourceState>> GetResourcesByDepartmentAsync(
        string departmentId,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<CustomerState?> GetCustomerStateAsync(
        string customerId,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<CustomerState>> GetCustomersByHealthAsync(
        CustomerHealthStatus status,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<OperationalState> TakeSnapshotAsync(
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<OrganizationStateDashboard> GetDashboardAsync(
        CancellationToken cancellationToken = default);
}
