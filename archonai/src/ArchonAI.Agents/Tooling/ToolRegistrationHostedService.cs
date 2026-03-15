using Microsoft.Extensions.Hosting;
using ArchonAI.Core.Interfaces.Tooling;

namespace ArchonAI.Agents.Tooling;

public sealed class ToolRegistrationHostedService : IHostedService
{
    private readonly IAgentToolRegistry _registry;
    private readonly IEnumerable<IAgentTool> _tools;

    public ToolRegistrationHostedService(IAgentToolRegistry registry, IEnumerable<IAgentTool> tools)
    {
        _registry = registry;
        _tools = tools;
    }

    public global::System.Threading.Tasks.Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (IAgentTool tool in _tools)
        {
            _registry.RegisterTool(tool);
        }

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task StopAsync(CancellationToken cancellationToken)
        => global::System.Threading.Tasks.Task.CompletedTask;
}
