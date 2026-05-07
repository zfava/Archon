using ArchonAI.Common;
using ArchonAI.Core.Interfaces;
using ArchonAI.Persistence.Stores;
using ArchonAI.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Persistence;

public static class DependencyInjection
{
    /// <summary>
    /// Registers all PostgreSQL-backed stores as named singletons and wires
    /// config-driven factory delegates that select between Postgres and in-memory
    /// implementations based on the <see cref="PersistenceOptions.ConnectionString"/>.
    ///
    /// IMPORTANT: Call this AFTER the existing in-memory service registrations
    /// (AddArchonAISecurity, AddArchonAIObservability, AddArchonAIIdentity, and the
    /// Program.cs singletons) so that the in-memory concrete types are already registered.
    /// This method removes the old interface registrations and replaces them with
    /// factory delegates that choose the correct backend.
    /// </summary>
    public static IServiceCollection AddArchonAIPersistence(this IServiceCollection services)
    {
        services.AddOptions<PersistenceOptions>()
            .BindConfiguration(PersistenceOptions.SectionName);

        // ── Domain stores (21) ───────────────────────────────────────
        services.AddSingleton<PostgresAuditLogStore>();
        services.AddSingleton<PostgresRbacStore>();
        services.AddSingleton<PostgresGovernanceStore>();
        services.AddSingleton<PostgresTrustTierStore>();
        services.AddSingleton<PostgresDecisionStore>();
        services.AddSingleton<PostgresFinancialConsequenceStore>();
        services.AddSingleton<PostgresScenarioStore>();
        services.AddSingleton<PostgresExceptionIntelligenceStore>();
        services.AddSingleton<PostgresOutcomeLearningStore>();
        services.AddSingleton<PostgresOperationalTwinStore>();
        services.AddSingleton<PostgresEnterpriseMemoryStore>();
        services.AddSingleton<PostgresMonitoringDashboardStore>();
        services.AddSingleton<PostgresHeroWorkflowStore>();
        services.AddSingleton<PostgresPolicySimulationStore>();
        services.AddSingleton<PostgresProofAnalyticsStore>();
        services.AddSingleton<PostgresActionSafetyStore>();
        services.AddSingleton<PostgresInspectionStore>();
        services.AddSingleton<PostgresAgentRegistryStore>();
        services.AddSingleton<PostgresControlPlaneStore>();
        services.AddSingleton<PostgresAgentCapabilityRegistryStore>();
        services.AddSingleton<PostgresControlPlaneAlertStore>();

        ReplaceWithFactory<IAuditLogService, PostgresAuditLogStore>(services);
        ReplaceWithFactory<IRbacService, PostgresRbacStore>(services);
        ReplaceWithFactory<IGovernanceService, PostgresGovernanceStore>(services);
        ReplaceWithFactory<ITrustTierService, PostgresTrustTierStore>(services);
        ReplaceWithFactory<IDecisionService, PostgresDecisionStore>(services);
        ReplaceWithFactory<IFinancialConsequenceService, PostgresFinancialConsequenceStore>(services);
        ReplaceWithFactory<IScenarioService, PostgresScenarioStore>(services);
        ReplaceWithFactory<IExceptionIntelligenceService, PostgresExceptionIntelligenceStore>(services);
        ReplaceWithFactory<IOutcomeLearningService, PostgresOutcomeLearningStore>(services);
        ReplaceWithFactory<IOperationalTwinService, PostgresOperationalTwinStore>(services);
        ReplaceWithFactory<IEnterpriseMemoryService, PostgresEnterpriseMemoryStore>(services);
        ReplaceWithFactory<IMonitoringDashboardService, PostgresMonitoringDashboardStore>(services);
        ReplaceWithFactory<IHeroWorkflowService, PostgresHeroWorkflowStore>(services);
        ReplaceWithFactory<IPolicySimulationService, PostgresPolicySimulationStore>(services);
        ReplaceWithFactory<IProofAnalyticsService, PostgresProofAnalyticsStore>(services);
        ReplaceWithFactory<IActionSafetyService, PostgresActionSafetyStore>(services);
        ReplaceWithFactory<IInspectionService, PostgresInspectionStore>(services);
        ReplaceWithFactory<IAgentRegistryRepository, PostgresAgentRegistryStore>(services);
        ReplaceWithFactory<IControlPlaneRepository, PostgresControlPlaneStore>(services);
        ReplaceWithFactory<IAgentCapabilityRegistry, PostgresAgentCapabilityRegistryStore>(services);
        ReplaceWithFactory<IControlPlaneAlertStore, PostgresControlPlaneAlertStore>(services);

        // ── Identity stores (9) ──────────────────────────────────────
        services.AddSingleton<PostgresUserStore>();
        services.AddSingleton<PostgresOrganizationStore>();
        services.AddSingleton<PostgresMembershipStore>();
        services.AddSingleton<PostgresRefreshTokenStore>();
        services.AddSingleton<PostgresInviteTokenStore>();
        services.AddSingleton<PostgresMfaStore>();
        services.AddSingleton<PostgresTenantAuthConfigStore>();
        services.AddSingleton<PostgresExternalIdentityLinkStore>();
        services.AddSingleton<PostgresOidcLoginSessionStore>();

        ReplaceWithFactory<IUserStore, PostgresUserStore>(services);
        ReplaceWithFactory<IOrganizationStore, PostgresOrganizationStore>(services);
        ReplaceWithFactory<IMembershipStore, PostgresMembershipStore>(services);
        ReplaceWithFactory<IRefreshTokenStore, PostgresRefreshTokenStore>(services);
        ReplaceWithFactory<IInviteTokenStore, PostgresInviteTokenStore>(services);
        ReplaceWithFactory<IMfaStore, PostgresMfaStore>(services);
        ReplaceWithFactory<ITenantAuthConfigStore, PostgresTenantAuthConfigStore>(services);
        ReplaceWithFactory<IExternalIdentityLinkStore, PostgresExternalIdentityLinkStore>(services);
        ReplaceWithFactory<IOidcLoginSessionStore, PostgresOidcLoginSessionStore>(services);

        return services;
    }

    private static void ReplaceWithFactory<TInterface, TPostgres>(IServiceCollection services)
        where TInterface : class
        where TPostgres : class, TInterface
    {
        // Capture the last existing descriptor so we can resolve the in-memory fallback
        ServiceDescriptor? inMemoryDescriptor = null;
        for (int i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(TInterface))
            {
                inMemoryDescriptor ??= services[i];
                services.RemoveAt(i);
            }
        }

        services.AddSingleton<TInterface>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PersistenceOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                return sp.GetRequiredService<TPostgres>();
            }

            // ── Environment-aware fallback gate ──────────────────────────
            // In production-like environments, silently falling back to in-memory
            // for enterprise-critical stores is a dangerous misconfiguration.
            var posture = sp.GetService<EnvironmentPosture>();
            var logger = sp.GetRequiredService<ILogger<PersistenceOptions>>();
            posture?.GuardInMemoryFallback(
                $"Persistence:{typeof(TInterface).Name}",
                $"{PersistenceOptions.SectionName}:ConnectionString",
                logger);

            // Resolve the original in-memory implementation (dev/test only)
            if (inMemoryDescriptor?.ImplementationType is not null)
            {
                return (TInterface)sp.GetRequiredService(inMemoryDescriptor.ImplementationType);
            }

            if (inMemoryDescriptor?.ImplementationFactory is not null)
            {
                return (TInterface)inMemoryDescriptor.ImplementationFactory(sp);
            }

            if (inMemoryDescriptor?.ImplementationInstance is not null)
            {
                return (TInterface)inMemoryDescriptor.ImplementationInstance;
            }

            throw new InvalidOperationException(
                $"No persistence connection string configured and no in-memory fallback found for {typeof(TInterface).Name}. " +
                $"Set {PersistenceOptions.SectionName}:ConnectionString or register an in-memory implementation.");
        });
    }
}
