using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.RuntimeHealth;
using ArchonAI.Runtime.Execution;
using ArchonAI.Runtime.Health;
using ArchonAI.Runtime.HostedServices;
using ArchonAI.Workflow;
using ArchonAI.WorkflowRuntime;
using ArchonAI.TaskRuntime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Runtime;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIRuntime(
        this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddOptions<RuntimeOptions>()
            .BindConfiguration("Runtime");

        if (configuration is not null)
        {
            services.Configure<RuntimeHealthOptions>(
                configuration.GetSection(RuntimeHealthOptions.SectionName));
        }
        else
        {
            services.Configure<RuntimeHealthOptions>(_ => { });
        }

        services.AddSingleton<IWorkflowEngine, WorkflowEngine>();
        services.AddArchonAIWorkflowRuntime();
        services.AddArchonAITaskRuntime();
        services.AddSingleton<ITaskExecutionManager, TaskExecutionManager>();
        services.AddScoped<IRuntime, AgentRuntime>();
        services.AddSingleton<IRuntimeHealthManager, RuntimeHealthManager>();
        services.AddHostedService<AgentRegistrationHostedService>();
        services.AddHostedService<RuntimeHealthMonitorService>();
        return services;
    }
}
