namespace ArchonAI.Core.Interfaces;

public interface ITenantResourceGovernor
{
    global::System.Threading.Tasks.Task<bool> TryAcquirePlanningSlotAsync(string tenantId, CancellationToken cancellationToken = default);

    void ReleasePlanningSlot(string tenantId);

    bool CanCreateTaskCount(string tenantId, int taskCount);
}
