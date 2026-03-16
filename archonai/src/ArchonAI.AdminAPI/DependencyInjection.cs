using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.AdminAPI;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIAdmin(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<AdminOptions>(configuration.GetSection(AdminOptions.SectionName));
        }
        else
        {
            services.Configure<AdminOptions>(_ => { });
        }

        services.AddSingleton<IAdminService, AdminService>();

        return services;
    }
}
