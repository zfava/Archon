using ArchonAI.Infrastructure.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArchonAI.Tests.Secrets;

[Trait("Category", "Unit")]
public sealed class AzureKeyVaultSecretProviderTests : IDisposable
{
    [Fact]
    public void Constructor_MissingVaultUri_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            new AzureKeyVaultSecretProvider(config, NullLogger<AzureKeyVaultSecretProvider>.Instance));
    }

    [Fact]
    public void SupportsRotation_ReturnsTrue()
    {
        using var provider = CreateProvider();
        Assert.True(provider.SupportsRotation);
    }

    [Fact]
    public void GetSecret_WhenSdkNotAvailable_ReturnsNull()
    {
        // When Azure SDK assemblies are not loaded, the provider degrades gracefully
        using var provider = CreateProvider();
        var result = provider.GetSecret("test-key");
        Assert.Null(result);
    }

    [Fact]
    public void GetRequiredSecret_WhenSdkNotAvailable_ThrowsInvalidOperation()
    {
        using var provider = CreateProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredSecret("test-key"));
    }

    [Fact]
    public void OnSecretChanged_RegisterAndUnregister_Works()
    {
        using var provider = CreateProvider();

        bool callbackFired = false;
        var subscription = provider.OnSecretChanged(key => callbackFired = true);
        Assert.NotNull(subscription);

        subscription.Dispose();
        Assert.False(callbackFired);
    }

    [Fact]
    public void Constructor_WithCustomPollingInterval_DoesNotThrow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Azure:KeyVault:VaultUri"] = "https://my-vault.vault.azure.net",
                ["Azure:KeyVault:PollingIntervalSeconds"] = "600"
            })
            .Build();

        using var provider = new AzureKeyVaultSecretProvider(config, NullLogger<AzureKeyVaultSecretProvider>.Instance);
        Assert.True(provider.SupportsRotation);
    }

    private static AzureKeyVaultSecretProvider CreateProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Azure:KeyVault:VaultUri"] = "https://test-vault.vault.azure.net",
                ["Azure:KeyVault:PollingIntervalSeconds"] = "300"
            })
            .Build();

        return new AzureKeyVaultSecretProvider(config, NullLogger<AzureKeyVaultSecretProvider>.Instance);
    }

    public void Dispose() { }
}
