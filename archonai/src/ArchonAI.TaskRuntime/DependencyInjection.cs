using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.TaskRuntime;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAITaskRuntime(this IServiceCollection services)
    {
        services.AddOptions<TaskRuntimeOptions>()
            .BindConfiguration("TaskRuntime");

        services.AddSingleton<ITaskExecutionEngine, TaskExecutionEngine>();
        return services;
    }
}
