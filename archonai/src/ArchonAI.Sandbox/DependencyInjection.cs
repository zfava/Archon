using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Sandbox;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAISandbox(this IServiceCollection services)
    {
        services.AddOptions<SandboxOptions>()
            .BindConfiguration("Sandbox");

        services.AddSingleton<IAgentSandboxManager, AgentSandboxManager>();
        return services;
    }
}
