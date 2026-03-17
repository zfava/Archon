using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Trace;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAITrace(this IServiceCollection services)
    {
        services.AddOptions<TraceOptions>()
            .BindConfiguration("Trace");

        services.AddSingleton<ITraceStore, DurableTraceStore>();
        return services;
    }
}
