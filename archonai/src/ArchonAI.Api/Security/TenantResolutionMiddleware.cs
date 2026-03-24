using ArchonAI.Core.Interfaces;

namespace ArchonAI.Api.Security;

public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IMultiTenantContext tenantContext)
    {
        var tenantClaim = context.User?.FindFirst("tenant_id")?.Value;
        if (!string.IsNullOrEmpty(tenantClaim))
        {
            using var scope = tenantContext.BeginTenantScope(tenantClaim);
            await _next(context);
            return;
        }

        await _next(context);
    }
}
