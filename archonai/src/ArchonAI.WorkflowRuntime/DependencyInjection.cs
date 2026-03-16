using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.WorkflowRuntime;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIWorkflowRuntime(this IServiceCollection services)
    {
        services.AddOptions<WorkflowRuntimeOptions>()
            .BindConfiguration("WorkflowRuntime");

        services.AddSingleton<IWorkflowExecutionEngine, WorkflowExecutionEngine>();
        return services;
    }
}
