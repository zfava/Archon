namespace ArchonAI.Core.Interfaces;

public interface IMultiTenantContext
{
    string CurrentTenantId { get; }

    IDisposable BeginTenantScope(string tenantId);
}
