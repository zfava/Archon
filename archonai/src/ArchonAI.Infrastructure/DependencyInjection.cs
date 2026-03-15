using ArchonAI.Core.Interfaces;
using ArchonAI.Evaluation;
using ArchonAI.Infrastructure.Eventing;
using ArchonAI.Infrastructure.Connectors;
using ArchonAI.Infrastructure.Memory;
using ArchonAI.Infrastructure.Services;
using ArchonAI.Knowledge;
using ArchonAI.Models;
using ArchonAI.Policy;
using ArchonAI.Reasoner;
using ArchonAI.Scheduler;
using ArchonAI.Simulation;
using ArchonAI.StrategicPlanner;
using ArchonAI.Trace;
using ArchonAI.Telemetry;
using ArchonAI.Patterns;
using ArchonAI.Strategy;
using ArchonAI.Governance;
using ArchonAI.Supervisor;
using ArchonAI.Sandbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArchonAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIInfrastructure(this IServiceCollection services)
    {
        services.AddOptions<MemoryPersistenceOptions>()
            .BindConfiguration("MemoryPersistence");

        services.AddOptions<EventBusOptions>()
            .BindConfiguration("EventBus");

        services.AddArchonAIKnowledge();
        services.AddArchonAIEvaluation();
        services.AddArchonAIPolicy();
        services.AddArchonAISimulation();
        services.AddArchonAIScheduler();
        services.AddArchonAIModels();
        services.AddArchonAITrace();
        services.AddArchonAITelemetry();
        services.AddArchonAIPatterns();
        services.AddArchonAIStrategy();
        services.AddArchonAIGovernance();
        services.AddArchonAISupervisor();
        services.AddArchonAISandbox();

        services.AddSingleton<IPlanningFeedbackStore, InMemoryPlanningFeedbackStore>();
        services.AddSingleton<IConnector, DefaultConnector>();
        services.AddScoped<IStrategicPlanner, StrategicPlanningEngine>();
        services.AddScoped<IPlanner, PlannerService>();
        services.AddScoped<IReasoner, ReasoningEngine>();

        services.AddSingleton<IMemoryRecordRepository, PostgresMemoryRecordRepository>();
        services.AddSingleton<PersistentMemoryStore>();
        services.AddSingleton<InMemoryStore>();
        services.AddSingleton<IMemoryStore>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MemoryPersistenceOptions>>().Value;
            return string.IsNullOrWhiteSpace(options.ConnectionString)
                ? serviceProvider.GetRequiredService<InMemoryStore>()
                : serviceProvider.GetRequiredService<PersistentMemoryStore>();
        });

        services.AddSingleton<InMemoryEventBus>();
        services.AddSingleton<NatsEventBus>();
        services.AddSingleton<IEventBus>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<EventBusOptions>>().Value;
            return options.UseNats
                ? serviceProvider.GetRequiredService<NatsEventBus>()
                : serviceProvider.GetRequiredService<InMemoryEventBus>();
        });

        return services;
    }
}
