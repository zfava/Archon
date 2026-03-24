using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace ArchonAI.MultiTenant;

public sealed class TenantResourceGovernor : ITenantResourceGovernor
{
    private readonly ConcurrentDictionary<string, int> _activePlanningSlots = new(StringComparer.OrdinalIgnoreCase);
    private readonly MultiTenantOptions _options;

    public TenantResourceGovernor(IOptions<MultiTenantOptions> options)
    {
        _options = options.Value;
    }

    public global::System.Threading.Tasks.Task<bool> TryAcquirePlanningSlotAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string normalized = NormalizeTenant(tenantId);

        while (true)
        {
            int current = _activePlanningSlots.GetOrAdd(normalized, 0);
            if (current >= _options.MaxConcurrentPlansPerTenant)
            {
                return global::System.Threading.Tasks.Task.FromResult(false);
            }

            if (_activePlanningSlots.TryUpdate(normalized, current + 1, current))
            {
                return global::System.Threading.Tasks.Task.FromResult(true);
            }
        }
    }

    public void ReleasePlanningSlot(string tenantId)
    {
        string normalized = NormalizeTenant(tenantId);
        while (true)
        {
            int current = _activePlanningSlots.GetOrAdd(normalized, 0);
            if (current <= 0)
            {
                _activePlanningSlots[normalized] = 0;
                return;
            }

            if (_activePlanningSlots.TryUpdate(normalized, current - 1, current))
            {
                return;
            }
        }
    }

    public bool CanCreateTaskCount(string tenantId, int taskCount)
    {
        _ = NormalizeTenant(tenantId);
        return taskCount <= _options.MaxTasksPerPlanPerTenant;
    }

    private string NormalizeTenant(string tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId) ? _options.DefaultTenantId : tenantId.Trim();
    }
}
