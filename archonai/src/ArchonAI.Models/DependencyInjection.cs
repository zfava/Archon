using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Models;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIModels(this IServiceCollection services)
    {
        services.AddOptions<ModelProviderOptions>()
            .BindConfiguration("ModelProviders");

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
