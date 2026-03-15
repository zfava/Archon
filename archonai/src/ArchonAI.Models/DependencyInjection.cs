using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Models;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIModels(this IServiceCollection services)
    {
        services.AddOptions<ModelProviderOptions>()
            .BindConfiguration("ModelProviders");

        services.AddHttpClient();

        services.AddSingleton<OpenAiModelProvider>();
        services.AddSingleton<AzureOpenAiModelProvider>();
        services.AddSingleton<AnthropicModelProvider>();
        services.AddSingleton<LocalModelProvider>();

        services.AddSingleton<IModelProvider, CompositeModelProvider>();
        return services;
    }
}
