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

        services.AddSingleton<IAgentIdentityStore, InMemoryAgentIdentityStore>();

        // User/org/membership stores
        services.AddSingleton<IUserStore, InMemoryUserStore>();
        services.AddSingleton<IOrganizationStore, InMemoryOrganizationStore>();
        services.AddSingleton<IMembershipStore, InMemoryMembershipStore>();
        services.AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
        services.AddSingleton<IInviteTokenStore, InMemoryInviteTokenStore>();

        // Auth services
        services.AddSingleton<TokenService>();
        services.AddSingleton<AuthenticationService>();

        return services;
    }
}
