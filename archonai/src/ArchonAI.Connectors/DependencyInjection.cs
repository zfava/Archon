using ArchonAI.Connectors.Framework;
using ArchonAI.Connectors.GoogleWorkspace;
using ArchonAI.Connectors.HubSpot;
using ArchonAI.Connectors.Implementations;
using ArchonAI.Connectors.Microsoft365;
using ArchonAI.Connectors.QuickBooks;
using ArchonAI.Connectors.Salesforce;
using ArchonAI.Connectors.Slack;
using ArchonAI.Core.Interfaces;
using ArchonAI.Infrastructure.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Connectors;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIConnectors(this IServiceCollection services, IConfiguration? configuration = null)
    {
        // CRM connector
        services.AddHttpClient("CRM");
        services.AddSingleton<ICrmConnector>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("CRM");
            return new CrmConnector(
                httpClient,
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CrmConnector>>());
        });

        // ERP connector
        services.AddHttpClient("ERP");
        services.AddSingleton<IErpConnector>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("ERP");
            return new ErpConnector(
                httpClient,
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ErpConnector>>());
        });

        // Messaging connector
        services.AddHttpClient("Messaging");
        services.AddSingleton<IMessagingConnector>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("Messaging");
            return new MessagingConnector(
                httpClient,
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MessagingConnector>>());
        });

        // Financial connector
        services.AddHttpClient("Financial");
        services.AddSingleton<IFinancialConnector>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("Financial");
            return new FinancialConnector(
                httpClient,
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FinancialConnector>>());
        });

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
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SalesforceOptions>>(),
                resilienceRegistry: sp.GetService<ConnectorResilienceRegistry>(),
                shadowMetrics: sp.GetRequiredService<ConnectorShadowMetrics>());
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
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HubSpotOptions>>(),
                sp.GetRequiredService<ConnectorShadowMetrics>());
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
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<QuickBooksOptions>>(),
                sp.GetRequiredService<ConnectorShadowMetrics>());
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
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SlackOptions>>(),
                sp.GetRequiredService<ConnectorShadowMetrics>());
        });
        services.AddSingleton<IConnector>(sp => sp.GetRequiredService<ISlackConnector>());

        // Google Workspace connector
        if (configuration is not null)
        {
            services.Configure<GoogleWorkspaceOptions>(configuration.GetSection(GoogleWorkspaceOptions.SectionName));
        }
        else
        {
            services.Configure<GoogleWorkspaceOptions>(_ => { });
        }

        services.AddHttpClient("GoogleWorkspace");
        services.AddSingleton<IGoogleWorkspaceConnector>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("GoogleWorkspace");
            return new GoogleWorkspaceConnector(
                httpClient,
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GoogleWorkspaceConnector>>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GoogleWorkspaceOptions>>(),
                sp.GetRequiredService<ConnectorShadowMetrics>());
        });
        services.AddSingleton<IConnector>(sp => sp.GetRequiredService<IGoogleWorkspaceConnector>());

        // Microsoft 365 connector
        if (configuration is not null)
        {
            services.Configure<Microsoft365Options>(configuration.GetSection(Microsoft365Options.SectionName));
        }
        else
        {
            services.Configure<Microsoft365Options>(_ => { });
        }

        services.AddHttpClient("Microsoft365");
        services.AddSingleton<IMicrosoft365Connector>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("Microsoft365");
            return new Microsoft365Connector(
                httpClient,
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Microsoft365Connector>>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft365Options>>(),
                sp.GetRequiredService<ConnectorShadowMetrics>());
        });
        services.AddSingleton<IConnector>(sp => sp.GetRequiredService<IMicrosoft365Connector>());

        // Shadow metrics for in-process connector counter reads
        services.AddSingleton<ConnectorShadowMetrics>();

        // Integration control center
        services.AddSingleton<IIntegrationControlService, IntegrationControlService>();

        // Register connector resilience pipelines (one per named connector)
        services.AddSingleton<ConnectorResilienceRegistry>();

        return services;
    }
}
