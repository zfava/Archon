using ArchonAI.Core.Interfaces;
using ArchonAI.Runtime.Execution;
using ArchonAI.Runtime.HostedServices;
using ArchonAI.Workflow;
using ArchonAI.WorkflowRuntime;
using ArchonAI.TaskRuntime;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Runtime;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIRuntime(this IServiceCollection services)
    {
        services.AddOptions<RuntimeOptions>()
            .BindConfiguration("Runtime");

        services.AddSingleton<IWorkflowEngine, WorkflowEngine>();
        services.AddArchonAIWorkflowRuntime();
        services.AddArchonAITaskRuntime();
        services.AddSingleton<ITaskExecutionManager, TaskExecutionManager>();
        services.AddScoped<IRuntime, AgentRuntime>();
        services.AddHostedService<AgentRegistrationHostedService>();
        return services;
    }
}
