using ArchonAI.Core.Interfaces;
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

        // User/org/membership stores – single durable store implements all interfaces
        services.AddSingleton<DurableIdentityStore>();
        services.AddSingleton<IUserStore>(sp => sp.GetRequiredService<DurableIdentityStore>());
        services.AddSingleton<IOrganizationStore>(sp => sp.GetRequiredService<DurableIdentityStore>());
        services.AddSingleton<IMembershipStore>(sp => sp.GetRequiredService<DurableIdentityStore>());
        services.AddSingleton<IRefreshTokenStore>(sp => sp.GetRequiredService<DurableIdentityStore>());
        services.AddSingleton<IInviteTokenStore>(sp => sp.GetRequiredService<DurableIdentityStore>());

        // Auth services
        services.AddSingleton<TokenService>();
        services.AddSingleton<AuthenticationService>();

        // OIDC federation stores
        services.AddSingleton<ITenantAuthConfigStore, InMemoryTenantAuthConfigStore>();
        services.AddSingleton<IExternalIdentityLinkStore, InMemoryExternalIdentityLinkStore>();

        // OIDC token exchange service
        services.AddOptions<OidcOptions>()
            .BindConfiguration(OidcOptions.SectionName);
        services.AddSingleton<OidcTokenExchangeService>();

        // SAML stub (not yet implemented — interface wired for future extension)
        services.AddSingleton<ISamlAuthenticationHandler, NotImplementedSamlHandler>();

        return services;
    }
}
