using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ArchonAI.Runtime.HostedServices;

/// <summary>
/// Registers all discovered agents with the runtime at startup.
/// </summary>
public sealed class AgentRegistrationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AgentRegistrationHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async global::System.Threading.Tasks.Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();

        var runtime = scope.ServiceProvider.GetRequiredService<IRuntime>();
        var agents = scope.ServiceProvider.GetServices<IAgent>();

        foreach (IAgent agent in agents)
        {
            await runtime.RegisterAgentAsync(agent.Describe(), cancellationToken);
        }
    }

    public global::System.Threading.Tasks.Task StopAsync(CancellationToken cancellationToken)
        => global::System.Threading.Tasks.Task.CompletedTask;
}
