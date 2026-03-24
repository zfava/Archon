using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Infrastructure.Resilience;

/// <summary>
/// Registers resilience services: options, state publisher, pipeline factory.
/// </summary>
public static class ResilienceDependencyInjection
{
    public static IServiceCollection AddArchonAIResilience(this IServiceCollection services)
    {
        services.AddOptions<ResilienceOptions>()
            .BindConfiguration(ResilienceOptions.SectionName);

        services.AddSingleton<CircuitBreakerStatePublisher>();

        services.AddSingleton<ResiliencePipelineFactory>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ResilienceOptions>>().Value;
            var statePublisher = sp.GetRequiredService<CircuitBreakerStatePublisher>();
            var logger = sp.GetRequiredService<ILogger<ResiliencePipelineFactory>>();
            return new ResiliencePipelineFactory(options, statePublisher, logger);
        });

        return services;
    }
}
