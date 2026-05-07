using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ArchonAI.Enterprise.Tests.Security;

/// <summary>
/// Tests the CORS policy configuration that mirrors Gateway's GatewayPolicy.
/// Uses TestServer with the same AddCors/UseCors configuration as the real Gateway
/// to validate adversarial origin vectors against the actual middleware pipeline.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Security")]
public sealed class CorsAdversarialTests : IAsyncLifetime
{
    private const string AllowedOrigin = "https://app.archonai.com";
    private const string AllowedOriginAdmin = "https://admin.archonai.com";

    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    // Mirror the exact CORS policy from Gateway Program.cs
                    services.AddCors(options =>
                    {
                        options.AddPolicy("GatewayPolicy", policy =>
                        {
                            policy.WithOrigins(AllowedOrigin, AllowedOriginAdmin)
                                .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS")
                                .WithHeaders("Authorization", "Content-Type", "X-Correlation-Id", "X-Tenant-Id")
                                .AllowCredentials()
                                .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
                        });
                    });

                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseCors("GatewayPolicy");
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/health", async context =>
                        {
                            context.Response.ContentType = "application/json";
                            await context.Response.WriteAsync("{\"status\":\"ok\"}");
                        });
                        endpoints.MapPost("/api/v1/test", async context =>
                        {
                            context.Response.ContentType = "application/json";
                            await context.Response.WriteAsync("{\"result\":\"created\"}");
                        });
                    });
                });
            })
            .Build();

        await _host.StartAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    // ── 1. Allowed origin receives correct ACAO header ──

    [Fact]
    public async Task AllowedOrigin_ExactMatch_ReturnsAcaoHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", AllowedOrigin);

        var response = await _client.SendAsync(request);

        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        var acao = response.Headers.GetValues("Access-Control-Allow-Origin").Single();
        Assert.Equal(AllowedOrigin, acao);
    }

    // ── 2. Disallowed origin gets no ACAO header ──

    [Fact]
    public async Task DisallowedOrigin_NoAcaoHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "https://evil.com");

        var response = await _client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    // ── 3. Subdomain spoofing blocked ──

    [Fact]
    public async Task OriginSubdomainSpoofing_Blocked()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "https://app.archonai.com.evil.com");

        var response = await _client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    // ── 4. Null origin blocked ──

    [Fact]
    public async Task NullOrigin_Blocked()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "null");

        var response = await _client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    // ── 5. Wildcard origin with credentials not permitted ──

    [Fact]
    public async Task WildcardOriginWithCredentials_NotPermitted()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", AllowedOrigin);

        var response = await _client.SendAsync(request);

        // With AllowCredentials(), ACAO must be the exact origin, never "*"
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        var acao = response.Headers.GetValues("Access-Control-Allow-Origin").Single();
        Assert.NotEqual("*", acao);

        Assert.True(response.Headers.Contains("Access-Control-Allow-Credentials"));
        var creds = response.Headers.GetValues("Access-Control-Allow-Credentials").Single();
        Assert.Equal("true", creds);
    }

    // ── 6. Preflight OPTIONS returns correct headers ──

    [Fact]
    public async Task PreflightRequest_AllowedOrigin_Returns204WithHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "Authorization, Content-Type");

        var response = await _client.SendAsync(request);

        Assert.True(
            response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK,
            $"Expected 204 or 200 but got {response.StatusCode}");

        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.True(response.Headers.Contains("Access-Control-Allow-Methods"));
    }

    // ── 7. Preflight for disallowed origin returns no ACAO ──

    [Fact]
    public async Task PreflightRequest_DisallowedOrigin_NoAcaoHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", "https://attacker.com");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "Authorization");

        var response = await _client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    // ── 8. Preflight max-age is set ──

    [Fact]
    public async Task PreflightRequest_MaxAge_IsSet()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        Assert.True(response.Headers.Contains("Access-Control-Max-Age"));
        var maxAge = int.Parse(response.Headers.GetValues("Access-Control-Max-Age").Single());
        Assert.True(maxAge > 0, "Max-Age should be positive");
    }

    // ── 9. HTTP scheme spoofing blocked (http vs https) ──

    [Fact]
    public async Task HttpSchemeSpoofing_Blocked()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "http://app.archonai.com");

        var response = await _client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    // ── 10. Port-appended origin blocked ──

    [Fact]
    public async Task PortAppendedOrigin_Blocked()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "https://app.archonai.com:8443");

        var response = await _client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    // ── 11. Second allowed origin works ──

    [Fact]
    public async Task SecondAllowedOrigin_ReturnsAcaoHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", AllowedOriginAdmin);

        var response = await _client.SendAsync(request);

        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        var acao = response.Headers.GetValues("Access-Control-Allow-Origin").Single();
        Assert.Equal(AllowedOriginAdmin, acao);
    }

    // ── 12. Vary header includes Origin for caching correctness ──

    [Fact]
    public async Task VaryHeader_IncludesOrigin()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", AllowedOrigin);

        var response = await _client.SendAsync(request);

        Assert.True(response.Headers.Contains("Vary") || response.Content.Headers.Contains("Vary"),
            "Response should include Vary header for CORS caching correctness");

        var vary = response.Headers.Contains("Vary")
            ? string.Join(", ", response.Headers.GetValues("Vary"))
            : string.Join(", ", response.Content.Headers.GetValues("Vary"));
        Assert.Contains("Origin", vary, StringComparison.OrdinalIgnoreCase);
    }
}
