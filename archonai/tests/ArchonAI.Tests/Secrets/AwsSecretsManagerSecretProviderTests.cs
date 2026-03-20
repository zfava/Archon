using ArchonAI.Infrastructure.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArchonAI.Tests.Secrets;

[Trait("Category", "Unit")]
public sealed class AwsSecretsManagerSecretProviderTests : IDisposable
{
    [Fact]
    public void Constructor_MissingRegion_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            new AwsSecretsManagerSecretProvider(config, NullLogger<AwsSecretsManagerSecretProvider>.Instance));
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
        // When AWS SDK assemblies are not loaded, the provider degrades gracefully
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
    public void Constructor_WithPrefix_DoesNotThrow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Aws:SecretsManager:Region"] = "us-east-1",
                ["Aws:SecretsManager:SecretNamePrefix"] = "archonai/prod",
                ["Aws:SecretsManager:PollingIntervalSeconds"] = "600"
            })
            .Build();

        using var provider = new AwsSecretsManagerSecretProvider(config, NullLogger<AwsSecretsManagerSecretProvider>.Instance);
        Assert.True(provider.SupportsRotation);
    }

    private static AwsSecretsManagerSecretProvider CreateProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Aws:SecretsManager:Region"] = "us-east-1",
                ["Aws:SecretsManager:PollingIntervalSeconds"] = "300"
            })
            .Build();

        return new AwsSecretsManagerSecretProvider(config, NullLogger<AwsSecretsManagerSecretProvider>.Instance);
    }

    public void Dispose() { }
}
