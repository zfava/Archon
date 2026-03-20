using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Models;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIModels(this IServiceCollection services)
    {
        services.AddOptions<ModelProviderOptions>()
            .BindConfiguration("ModelProviders")
            .PostConfigure(opts =>
            {
                // Bind API keys from environment variables (takes precedence over appsettings)
                var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                if (!string.IsNullOrWhiteSpace(openAiKey))
                    opts.OpenAI.ApiKey = openAiKey;

                var anthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
                if (!string.IsNullOrWhiteSpace(anthropicKey))
                    opts.Anthropic.ApiKey = anthropicKey;

                var azureKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
                if (!string.IsNullOrWhiteSpace(azureKey))
                    opts.AzureOpenAI.ApiKey = azureKey;

                var azureEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
                if (!string.IsNullOrWhiteSpace(azureEndpoint))
                    opts.AzureOpenAI.Endpoint = azureEndpoint;
            });

        services.AddHttpClient("OpenAI");
        services.AddHttpClient("Anthropic");
        services.AddHttpClient("AzureOpenAI");
        services.AddHttpClient("Local");

        services.AddSingleton<OpenAiModelProvider>();
        services.AddSingleton<AzureOpenAiModelProvider>();
        services.AddSingleton<AnthropicModelProvider>();
        services.AddSingleton<LocalModelProvider>();

        services.AddSingleton<IModelProvider, CompositeModelProvider>();

        services.AddSingleton<ModelOutputValidator>();
        services.AddSingleton<ISystemPromptProvider, SystemPromptProvider>();

        services.AddHostedService<ModelProviderActivationService>();

        return services;
    }
}
