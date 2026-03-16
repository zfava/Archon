using ArchonAI.Connectors.HubSpot;
using ArchonAI.Connectors.Implementations;
using ArchonAI.Connectors.QuickBooks;
using ArchonAI.Connectors.Salesforce;
using ArchonAI.Connectors.Slack;
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

        // HubSpot connector
        if (configuration is not null)
        {
            services.Configure<HubSpotOptions>(configuration.GetSection(HubSpotOptions.SectionName));
        }
        else
        {
            services.Configure<HubSpotOptions>(_ => { });
        }

        services.AddHttpClient("HubSpot");
        services.AddSingleton<IHubSpotConnector>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("HubSpot");
            return new HubSpotConnector(
                httpClient,
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<HubSpotConnector>>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HubSpotOptions>>());
        });
        services.AddSingleton<IConnector>(sp => sp.GetRequiredService<IHubSpotConnector>());

        // QuickBooks connector
        if (configuration is not null)
        {
            services.Configure<QuickBooksOptions>(configuration.GetSection(QuickBooksOptions.SectionName));
        }
        else
        {
            services.Configure<QuickBooksOptions>(_ => { });
        }

        services.AddHttpClient("QuickBooks");
        services.AddSingleton<IQuickBooksConnector>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("QuickBooks");
            return new QuickBooksConnector(
                httpClient,
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<QuickBooksConnector>>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<QuickBooksOptions>>());
        });
        services.AddSingleton<IConnector>(sp => sp.GetRequiredService<IQuickBooksConnector>());

        // Slack connector
        if (configuration is not null)
        {
            services.Configure<SlackOptions>(configuration.GetSection(SlackOptions.SectionName));
        }
        else
        {
            services.Configure<SlackOptions>(_ => { });
        }

        services.AddHttpClient("Slack");
        services.AddSingleton<ISlackConnector>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("Slack");
            return new SlackConnector(
                httpClient,
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SlackConnector>>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SlackOptions>>());
        });
        services.AddSingleton<IConnector>(sp => sp.GetRequiredService<ISlackConnector>());

        return services;
    }
}
