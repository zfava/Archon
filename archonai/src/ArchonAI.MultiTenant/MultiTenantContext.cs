using System.Threading;
using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace ArchonAI.MultiTenant;

public sealed class MultiTenantContext : IMultiTenantContext
{
    private readonly MultiTenantOptions _options;
    private static readonly AsyncLocal<string?> TenantLocal = new();

    public MultiTenantContext(IOptions<MultiTenantOptions> options)
    {
        _options = options.Value;
    }

    public string CurrentTenantId => string.IsNullOrWhiteSpace(TenantLocal.Value)
        ? _options.DefaultTenantId
        : TenantLocal.Value!;

    public IDisposable BeginTenantScope(string tenantId)
    {
        string previous = CurrentTenantId;
        TenantLocal.Value = string.IsNullOrWhiteSpace(tenantId) ? _options.DefaultTenantId : tenantId.Trim();
        return new ScopeHandle(previous);
    }

    private sealed class ScopeHandle : IDisposable
    {
        private readonly string _previous;
        private bool _disposed;

        public ScopeHandle(string previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            TenantLocal.Value = _previous;
            _disposed = true;
        }
    }
}
