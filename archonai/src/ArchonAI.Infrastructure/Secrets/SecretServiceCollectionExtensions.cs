using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Infrastructure.Secrets;

public static class SecretServiceCollectionExtensions
{
    /// <summary>
    /// Registers secret providers based on configuration.
    /// Supports environment variables (default), file-based (Vault), or chained.
    /// </summary>
    public static IServiceCollection AddArchonAISecrets(this IServiceCollection services, IConfiguration configuration)
    {
        var vaultSecretsPath = configuration["Secrets:VaultPath"]
            ?? Environment.GetEnvironmentVariable("ARCHONAI_VAULT_SECRETS_PATH");

        if (!string.IsNullOrWhiteSpace(vaultSecretsPath))
        {
            // Vault Agent sidecar mode: file-based with env fallback
            services.AddSingleton<FileSecretProvider>(sp =>
                new FileSecretProvider(vaultSecretsPath, sp.GetRequiredService<ILogger<FileSecretProvider>>()));

            services.AddSingleton<EnvironmentSecretProvider>();

            services.AddSingleton<ISecretProvider>(sp =>
            {
                var fileProvider = sp.GetRequiredService<FileSecretProvider>();
                var envProvider = sp.GetRequiredService<EnvironmentSecretProvider>();
                var logger = sp.GetRequiredService<ILogger<ChainedSecretProvider>>();
                return new ChainedSecretProvider(new ISecretProvider[] { fileProvider, envProvider }, logger);
            });

            services.AddSingleton<ISecretRotationNotifier>(sp =>
                sp.GetRequiredService<FileSecretProvider>());
        }
        else
        {
            // Default: environment variable mode (K8s secrets via envFrom)
            services.AddSingleton<ISecretProvider, EnvironmentSecretProvider>();
        }

        return services;
    }
}
