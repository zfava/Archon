using ArchonAI.Agents.Agents;
using ArchonAI.Agents.Tooling;
using ArchonAI.Agents.Tooling.ConnectorTools;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Interfaces.Tooling;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Agents;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIAgentTooling(this IServiceCollection services)
    {
        services.AddSingleton<AgentToolingFramework>();
        services.AddSingleton<IAgentToolRegistry>(sp => sp.GetRequiredService<AgentToolingFramework>());
        services.AddSingleton<IAgentToolExecutor>(sp => sp.GetRequiredService<AgentToolingFramework>());

        services.AddSingleton<IAgentTool, InternalServiceTool>();
        services.AddSingleton<IAgentTool, MemoryQueryTool>();
        services.AddSingleton<IAgentTool, KnowledgeGraphQueryTool>();
        services.AddSingleton<IAgentTool, DataFabricQueryTool>();
        services.AddSingleton<IAgentTool, ExternalConnectorTool>();
        services.AddSingleton<IAgentTool, CrmConnectorTool>();
        services.AddSingleton<IAgentTool, ErpConnectorTool>();
        services.AddSingleton<IAgentTool, MessagingConnectorTool>();
        services.AddSingleton<IAgentTool, FinancialConnectorTool>();

        services.AddSingleton<IAgent, ToolEnabledAgent>();

        services.AddHostedService<ToolRegistrationHostedService>();
        return services;
    }
}
