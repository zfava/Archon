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
        var providers = new List<Func<IServiceProvider, ISecretProvider>>();
        var rotationNotifiers = new List<Func<IServiceProvider, ISecretRotationNotifier>>();

        // ── HashiCorp Vault (highest priority when configured) ──
        if (!string.IsNullOrWhiteSpace(configuration["Vault:Endpoint"]))
        {
            services.AddSingleton<HashiCorpVaultSecretProvider>();
            providers.Add(sp => sp.GetRequiredService<HashiCorpVaultSecretProvider>());
            rotationNotifiers.Add(sp => sp.GetRequiredService<HashiCorpVaultSecretProvider>());
        }

        // ── AWS Secrets Manager ──
        if (!string.IsNullOrWhiteSpace(configuration["Aws:SecretsManager:Region"]))
        {
            services.AddSingleton<AwsSecretsManagerSecretProvider>();
            providers.Add(sp => sp.GetRequiredService<AwsSecretsManagerSecretProvider>());
            rotationNotifiers.Add(sp => sp.GetRequiredService<AwsSecretsManagerSecretProvider>());
        }

        // ── Azure Key Vault ──
        if (!string.IsNullOrWhiteSpace(configuration["Azure:KeyVault:VaultUri"]))
        {
            services.AddSingleton<AzureKeyVaultSecretProvider>();
            providers.Add(sp => sp.GetRequiredService<AzureKeyVaultSecretProvider>());
            rotationNotifiers.Add(sp => sp.GetRequiredService<AzureKeyVaultSecretProvider>());
        }

        // ── Vault Agent sidecar mode: file-based ──
        var vaultSecretsPath = configuration["Secrets:VaultPath"]
            ?? Environment.GetEnvironmentVariable("ARCHONAI_VAULT_SECRETS_PATH");

        if (!string.IsNullOrWhiteSpace(vaultSecretsPath))
        {
            services.AddSingleton<FileSecretProvider>(sp =>
                new FileSecretProvider(vaultSecretsPath, sp.GetRequiredService<ILogger<FileSecretProvider>>()));
            providers.Add(sp => sp.GetRequiredService<FileSecretProvider>());
            rotationNotifiers.Add(sp => sp.GetRequiredService<FileSecretProvider>());
        }

        // ── Environment variables (always last fallback) ──
        services.AddSingleton<EnvironmentSecretProvider>();
        providers.Add(sp => sp.GetRequiredService<EnvironmentSecretProvider>());

        // ── Register the chained provider or single provider ──
        if (providers.Count > 1)
        {
            services.AddSingleton<ISecretProvider>(sp =>
            {
                var resolvedProviders = providers.Select(f => f(sp)).ToList();
                var logger = sp.GetRequiredService<ILogger<ChainedSecretProvider>>();
                return new ChainedSecretProvider(resolvedProviders, logger);
            });
        }
        else
        {
            services.AddSingleton<ISecretProvider, EnvironmentSecretProvider>();
        }

        // ── Register rotation notifiers ──
        if (rotationNotifiers.Count > 0)
        {
            // Register the first rotation notifier as the primary
            services.AddSingleton<ISecretRotationNotifier>(sp => rotationNotifiers[0](sp));
        }

        return services;
    }
}
