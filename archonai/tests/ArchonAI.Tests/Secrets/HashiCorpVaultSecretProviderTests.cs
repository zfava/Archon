using System.Net;
using System.Text.Json;
using ArchonAI.Infrastructure.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArchonAI.Tests.Secrets;

[Trait("Category", "Unit")]
public sealed class HashiCorpVaultSecretProviderTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler = new();

    [Fact]
    public void GetSecret_SuccessfulRetrieval_ReturnsValue()
    {
        _handler.SetupLoginResponse();
        _handler.SetupSecretResponse("my-secret", "secret-value-123", version: 1);

        using var provider = CreateProvider();
        var result = provider.GetSecret("my-secret");

        Assert.Equal("secret-value-123", result);
    }

    [Fact]
    public void GetSecret_VaultReturns500_ReturnsNull_GracefulDegradation()
    {
        _handler.SetupLoginResponse();
        _handler.SetupErrorResponse(HttpStatusCode.InternalServerError);

        using var provider = CreateProvider();
        var result = provider.GetSecret("my-secret");

        Assert.Null(result);
    }

    [Fact]
    public void GetSecret_VaultTimeout_ReturnsNull_GracefulDegradation()
    {
        _handler.SetupLoginResponse();
        _handler.SetupTimeoutResponse();

        using var provider = CreateProvider();
        var result = provider.GetSecret("my-secret");

        Assert.Null(result);
    }

    [Fact]
    public void GetRequiredSecret_NotFound_ThrowsInvalidOperation()
    {
        _handler.SetupLoginResponse();
        _handler.SetupErrorResponse(HttpStatusCode.NotFound);

        using var provider = CreateProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredSecret("missing-secret"));
    }

    [Fact]
    public void SupportsRotation_ReturnsTrue()
    {
        _handler.SetupLoginResponse();
        using var provider = CreateProvider();
        Assert.True(provider.SupportsRotation);
    }

    [Fact]
    public void OnSecretChanged_RegisterAndUnregister_Works()
    {
        _handler.SetupLoginResponse();
        using var provider = CreateProvider();

        bool callbackFired = false;
        var subscription = provider.OnSecretChanged(key => callbackFired = true);
        Assert.NotNull(subscription);

        subscription.Dispose();
        Assert.False(callbackFired);
    }

    [Fact]
    public void Constructor_MissingEndpoint_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vault:AppRoleRoleId"] = "role-id",
                ["Vault:AppRoleSecretId"] = "secret-id"
            })
            .Build();

        var factory = CreateHttpClientFactory();
        Assert.Throws<InvalidOperationException>(() =>
            new HashiCorpVaultSecretProvider(factory, config, NullLogger<HashiCorpVaultSecretProvider>.Instance));
    }

    [Fact]
    public void Constructor_MissingRoleId_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vault:Endpoint"] = "http://vault:8200",
                ["Vault:AppRoleSecretId"] = "secret-id"
            })
            .Build();

        var factory = CreateHttpClientFactory();
        Assert.Throws<InvalidOperationException>(() =>
            new HashiCorpVaultSecretProvider(factory, config, NullLogger<HashiCorpVaultSecretProvider>.Instance));
    }

    private HashiCorpVaultSecretProvider CreateProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vault:Endpoint"] = "http://vault:8200",
                ["Vault:MountPath"] = "secret",
                ["Vault:AppRoleRoleId"] = "test-role-id",
                ["Vault:AppRoleSecretId"] = "test-secret-id",
                ["Vault:RenewIntervalSeconds"] = "60"
            })
            .Build();

        return new HashiCorpVaultSecretProvider(
            CreateHttpClientFactory(),
            config,
            NullLogger<HashiCorpVaultSecretProvider>.Instance);
    }

    private IHttpClientFactory CreateHttpClientFactory()
    {
        return new MockHttpClientFactory(_handler);
    }

    public void Dispose()
    {
        _handler.Dispose();
    }

    private sealed class MockHttpClientFactory(MockHttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("http://vault:8200") };
    }

    internal sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
        private Func<HttpRequestMessage, HttpResponseMessage>? _defaultResponse;

        public void SetupLoginResponse(int leaseDuration = 3600)
        {
            _responses.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    auth = new { client_token = "test-token", lease_duration = leaseDuration }
                }))
            });
        }

        public void SetupSecretResponse(string key, string value, int version)
        {
            _defaultResponse = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    data = new
                    {
                        data = new Dictionary<string, string> { [key] = value },
                        metadata = new { version }
                    }
                }))
            };
        }

        public void SetupErrorResponse(HttpStatusCode status)
        {
            _defaultResponse = _ => new HttpResponseMessage(status);
        }

        public void SetupTimeoutResponse()
        {
            _defaultResponse = _ => throw new TaskCanceledException("Timeout");
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (_responses.Count > 0)
                return Task.FromResult(_responses.Dequeue()(request));

            if (_defaultResponse is not null)
                return Task.FromResult(_defaultResponse(request));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
