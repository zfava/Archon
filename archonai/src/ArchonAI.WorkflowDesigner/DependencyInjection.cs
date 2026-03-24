using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.WorkflowDesigner;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIWorkflowDesigner(this IServiceCollection services)
    {
        services.AddSingleton<IWorkflowDesignerService, WorkflowDesignerService>();
        return services;
    }
}
