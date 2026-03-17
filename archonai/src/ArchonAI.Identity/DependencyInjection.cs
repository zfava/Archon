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

        return services;
    }
}
