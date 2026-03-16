using ArchonAI.Connectors.Implementations;
using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Connectors;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIConnectors(this IServiceCollection services)
    {
        services.AddSingleton<ICrmConnector, CrmConnector>();
        services.AddSingleton<IErpConnector, ErpConnector>();
        services.AddSingleton<IMessagingConnector, MessagingConnector>();
        services.AddSingleton<IFinancialConnector, FinancialConnector>();

        services.AddSingleton<IConnector>(sp => sp.GetRequiredService<ICrmConnector>());
        services.AddSingleton<IConnector>(sp => sp.GetRequiredService<IErpConnector>());
        services.AddSingleton<IConnector>(sp => sp.GetRequiredService<IMessagingConnector>());
        services.AddSingleton<IConnector>(sp => sp.GetRequiredService<IFinancialConnector>());

        return services;
    }
}
