using ArchonAI.Core.Interfaces;
using ArchonAI.Evaluation;
using ArchonAI.Infrastructure.Eventing;
using ArchonAI.Infrastructure.Connectors;
using ArchonAI.Infrastructure.Memory;
using ArchonAI.Infrastructure.Resilience;
using ArchonAI.Infrastructure.Services;
using ArchonAI.Knowledge;
using ArchonAI.KnowledgeGraph;
using ArchonAI.Learning;
using ArchonAI.Optimization;
using ArchonAI.Models;
using ArchonAI.ModelRouter;
using ArchonAI.Policy;
using ArchonAI.Reasoner;
using ArchonAI.Scheduler;
using ArchonAI.Simulation;
using ArchonAI.StrategicPlanner;
using ArchonAI.Trace;
using ArchonAI.Telemetry;
using ArchonAI.Patterns;
using ArchonAI.PatternDiscovery;
using ArchonAI.DataFabric;
using ArchonAI.MultiTenant;
using ArchonAI.Strategy;
using ArchonAI.Governance;
using ArchonAI.Supervisor;
using ArchonAI.Sandbox;
using ArchonAI.Orchestrator;
using ArchonAI.Identity;
using ArchonAI.Context;
using ArchonAI.Perception;
using ArchonAI.Infrastructure.Cluster;
using ArchonAI.Infrastructure.Secrets;
using ArchonAI.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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

        // Resilience: circuit breakers, bulkheads, timeouts
        services.AddArchonAIResilience();

        services.AddArchonAIKnowledge();
        services.AddArchonAIKnowledgeGraph();
        services.AddArchonAIEvaluation();
        services.AddArchonAIPolicy();
        services.AddArchonAISimulation();
        services.AddArchonAIScheduler();
        services.AddArchonAIModels();
        services.AddArchonAIModelRouter();
        services.AddArchonAITrace();
        services.AddArchonAITelemetry();
        services.AddArchonAIPatterns();
        services.AddArchonAIPatternDiscovery();
        services.AddArchonAILearning();
        services.AddArchonAIOptimization();
        services.AddArchonAIDataFabric();
        services.AddArchonAIMultiTenant();
        services.AddArchonAIStrategy();
        services.AddArchonAIGovernance();
        services.AddArchonAISupervisor();
        services.AddArchonAISandbox();
        services.AddArchonAIOrchestrator();
        services.AddArchonAIIdentity();
        services.AddArchonAIContext();
        services.AddArchonAIPerception();

        // Secret provider and TOTP encryption (dedicated key, independent of JWT)
        services.TryAddSingleton<ISecretProvider, EnvironmentSecretProvider>();
        services.AddSingleton<DedicatedTotpSecretEncryptor>();
        services.AddSingleton<ITotpSecretEncryptor>(sp =>
        {
            var encryptor = sp.GetRequiredService<DedicatedTotpSecretEncryptor>();

            // Report TOTP encryption posture to the health check subsystem
            ArchonAI.Common.Observability.FallbackPostureHealthCheck.RecordPosture(
                "TotpEncryption",
                encryptor.HasDedicatedKey ? "DedicatedKey" : "JwtKeyFallback",
                isDurable: encryptor.HasDedicatedKey);

            return encryptor;
        });

        services.AddOptions<ClusterOptions>()
            .BindConfiguration(ClusterOptions.SectionName);
        services.AddOptions<ClusterNodeRegistrationOptions>()
            .BindConfiguration(ClusterNodeRegistrationOptions.SectionName);
        services.AddSingleton<IClusterCoordinator, ClusterCoordinator>();

        services.AddOptions<PersistenceOptions>()
            .BindConfiguration(PersistenceOptions.SectionName);
        services.AddSingleton<RetentionHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<RetentionHostedService>());

        services.AddSingleton<IPlanningFeedbackStore, InMemoryPlanningFeedbackStore>();
        services.AddSingleton<IConnector, DefaultConnector>();
        services.AddScoped<IStrategicPlanner, StrategicPlanningEngine>();
        services.AddSingleton<IGoalGenerator, GoalGenerator>();
        services.AddSingleton<ITaskGraphBuilder, TaskGraphBuilder>();
        services.AddScoped<IPlanner, PlannerService>();
        services.AddScoped<IReasoner, ReasoningEngine>();
        services.AddScoped<IEconomicEvaluator, EconomicEvaluator>();
        services.AddScoped<IOutcomeEvaluator, OutcomeEvaluator>();
        services.AddScoped<IExplanationEngine, ExplanationEngine>();
        services.AddSingleton<IStrategyStore, TenantStrategyStore>();

        services.AddSingleton<IMemoryRecordRepository, PostgresMemoryRecordRepository>();
        services.AddSingleton<PersistentMemoryStore>();
        services.AddSingleton<InMemoryStore>();
        services.AddSingleton<IMemoryStore>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MemoryPersistenceOptions>>().Value;
            IMemoryStore baseStore;
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                // ── Environment-aware fallback gate ──────────────────────────
                var posture = serviceProvider.GetService<ArchonAI.Common.EnvironmentPosture>();
                var memLogger = serviceProvider.GetRequiredService<ILogger<MemoryPersistenceOptions>>();
                posture?.GuardInMemoryFallback(
                    "MemoryStore",
                    "MemoryPersistence:ConnectionString",
                    memLogger);
                baseStore = serviceProvider.GetRequiredService<InMemoryStore>();
            }
            else
            {
                baseStore = serviceProvider.GetRequiredService<PersistentMemoryStore>();
            }

            var tenantContext = serviceProvider.GetRequiredService<IMultiTenantContext>();
            var tenantOptions = serviceProvider.GetRequiredService<IOptions<MultiTenantOptions>>();
            return new TenantMemoryStore(baseStore, tenantContext, tenantOptions);
        });

        services.AddSingleton<InMemoryEventBus>();
        services.AddSingleton<NatsEventBus>();
        services.AddSingleton<IEventBus>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<EventBusOptions>>().Value;
            IEventBus inner;
            if (options.UseNats)
            {
                inner = serviceProvider.GetRequiredService<NatsEventBus>();
            }
            else
            {
                // ── Environment-aware fallback gate ──────────────────────────
                var posture = serviceProvider.GetService<ArchonAI.Common.EnvironmentPosture>();
                var ebLogger = serviceProvider.GetRequiredService<ILogger<EventBusOptions>>();
                posture?.GuardInMemoryFallback(
                    "EventBus",
                    "EventBus:UseNats",
                    ebLogger);
                inner = serviceProvider.GetRequiredService<InMemoryEventBus>();
            }

            // Wrap with circuit breaker and timeout
            var pipelineFactory = serviceProvider.GetRequiredService<ResiliencePipelineFactory>();
            var pipeline = pipelineFactory.CreateEventBusPipeline();
            var logger = serviceProvider.GetRequiredService<ILogger<ResilientEventBus>>();
            return new ResilientEventBus(inner, pipeline, logger);
        });

        return services;
    }
}
