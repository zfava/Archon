using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Memory;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIMemory(
        this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<MemoryCompressionOptions>(
                configuration.GetSection(MemoryCompressionOptions.SectionName));
            services.Configure<MemoryRetrievalOptions>(
                configuration.GetSection(MemoryRetrievalOptions.SectionName));
        }
        else
        {
            services.Configure<MemoryCompressionOptions>(_ => { });
            services.Configure<MemoryRetrievalOptions>(_ => { });
        }

        services.AddSingleton<IMemoryCompressionEngine, MemoryCompressionEngine>();
        services.AddSingleton<IMemoryRetrievalOptimizer, MemoryRetrievalOptimizer>();
        services.AddSingleton<IOrganizationalMemoryStore, OrganizationalMemoryStore>();
        return services;
    }
}
