using ArchonAI.Connectors.Implementations;
using ArchonAI.Connectors.Salesforce;
using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Connectors;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIConnectors(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddSingleton<ICrmConnector, CrmConnector>();
        services.AddSingleton<IErpConnector, ErpConnector>();
        services.AddSingleton<IMessagingConnector, MessagingConnector>();
        services.AddSingleton<IFinancialConnector, FinancialConnector>();

        services.AddSingleton<IConnector>(sp => sp.GetRequiredService<ICrmConnector>());
        services.AddSingleton<IConnector>(sp => sp.GetRequiredService<IErpConnector>());
        services.AddSingleton<IConnector>(sp => sp.GetRequiredService<IMessagingConnector>());
        services.AddSingleton<IConnector>(sp => sp.GetRequiredService<IFinancialConnector>());

        // Salesforce connector
        if (configuration is not null)
        {
            services.Configure<SalesforceOptions>(configuration.GetSection(SalesforceOptions.SectionName));
        }
        else
        {
            services.Configure<SalesforceOptions>(_ => { });
        }

        services.AddHttpClient("Salesforce");
        services.AddSingleton<ISalesforceConnector>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("Salesforce");
            return new SalesforceConnector(
                httpClient,
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SalesforceConnector>>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SalesforceOptions>>());
        });
        services.AddSingleton<IConnector>(sp => sp.GetRequiredService<ISalesforceConnector>());

        return services;
    }
}
