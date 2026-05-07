using ArchonAI.Core.Interfaces;
using ArchonAI.Identity.Mfa;
using ArchonAI.Identity.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Identity;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIIdentity(this IServiceCollection services)
    {
        services.AddOptions<IdentityOptions>()
            .BindConfiguration("Identity");

        services.AddOptions<AuthenticationOptions>()
            .BindConfiguration(AuthenticationOptions.SectionName);

        services.AddSingleton<IAgentIdentityStore, DurableAgentIdentityStore>();

        // In-memory identity stores — registered as concrete types for the persistence
        // factory to capture. When PostgreSQL ConnectionString is configured,
        // AddArchonAIPersistence() replaces these with Postgres-backed implementations.
        services.AddSingleton<InMemoryUserStore>();
        services.AddSingleton<IUserStore>(sp => sp.GetRequiredService<InMemoryUserStore>());
        services.AddSingleton<InMemoryOrganizationStore>();
        services.AddSingleton<IOrganizationStore>(sp => sp.GetRequiredService<InMemoryOrganizationStore>());
        services.AddSingleton<InMemoryMembershipStore>();
        services.AddSingleton<IMembershipStore>(sp => sp.GetRequiredService<InMemoryMembershipStore>());
        services.AddSingleton<InMemoryRefreshTokenStore>();
        services.AddSingleton<IRefreshTokenStore>(sp => sp.GetRequiredService<InMemoryRefreshTokenStore>());
        services.AddSingleton<InMemoryInviteTokenStore>();
        services.AddSingleton<IInviteTokenStore>(sp => sp.GetRequiredService<InMemoryInviteTokenStore>());

        // MFA store
        services.AddSingleton<InMemoryMfaStore>();
        services.AddSingleton<IMfaStore>(sp => sp.GetRequiredService<InMemoryMfaStore>());

        // Auth services
        services.AddSingleton<TokenService>();
        services.AddSingleton<AuthenticationService>();

        // MFA services
        services.AddSingleton<TotpService>();
        services.AddSingleton<WebAuthnService>();
        services.AddSingleton<MfaChallengeService>();

        // OIDC federation stores
        services.AddSingleton<InMemoryTenantAuthConfigStore>();
        services.AddSingleton<ITenantAuthConfigStore>(sp => sp.GetRequiredService<InMemoryTenantAuthConfigStore>());
        services.AddSingleton<InMemoryExternalIdentityLinkStore>();
        services.AddSingleton<IExternalIdentityLinkStore>(sp => sp.GetRequiredService<InMemoryExternalIdentityLinkStore>());
        services.AddSingleton<InMemoryOidcLoginSessionStore>();
        services.AddSingleton<IOidcLoginSessionStore>(sp => sp.GetRequiredService<InMemoryOidcLoginSessionStore>());

        // OIDC token exchange service
        services.AddOptions<OidcOptions>()
            .BindConfiguration(OidcOptions.SectionName);
        services.AddSingleton<OidcTokenExchangeService>();

        // GDPR data subject rights (Articles 15, 17, 20)
        services.AddSingleton<IDataSubjectService, DataSubjectService>();

        return services;
    }
}
