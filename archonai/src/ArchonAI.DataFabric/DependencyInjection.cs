using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.DataFabric;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIDataFabric(this IServiceCollection services)
    {
        services.AddOptions<DataFabricOptions>()
            .BindConfiguration("DataFabric");

        services.AddSingleton<IDataFabricEngine, DataFabricEngine>();
        return services;
    }
}
