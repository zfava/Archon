using ArchonAI.Api.Security;
using ArchonAI.Identity;
using ArchonAI.Identity.Stores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArchonAI.Enterprise.Tests.Security;

/// <summary>
/// Security tests for input validation and injection prevention:
/// credential stuffing, SQL injection in search, XSS payloads,
/// boundary values, and malformed input handling.
/// </summary>
public sealed class InputValidationTests
{
    private AuthenticationService CreateAuth()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Jwt:SigningKey"] = "test-signing-key-that-is-at-least-32-bytes-long!!",
                ["Security:Jwt:Issuer"] = "ArchonAI",
                ["Security:Jwt:Audience"] = "ArchonAI.Api",
            })
            .Build();

        return new AuthenticationService(
            new InMemoryUserStore(),
            new InMemoryOrganizationStore(),
            new InMemoryMembershipStore(),
            new InMemoryRefreshTokenStore(),
            new InMemoryInviteTokenStore(),
            new TokenService(config),
            Options.Create(new AuthenticationOptions()),
            NullLogger<AuthenticationService>.Instance);
    }

    // ── SQL Injection Attempts ────────────────────────────────────────

    [Theory]
    [InlineData("admin' OR '1'='1")]
    [InlineData("'; DROP TABLE users; --")]
    [InlineData("admin'--")]
    [InlineData("' UNION SELECT * FROM users --")]
    public async Task SqlInjection_InEmail_FailsGracefully(string maliciousEmail)
    {
        var auth = CreateAuth();

        // Should not crash or return unintended results
        var result = await auth.LoginAsync(maliciousEmail, "password");
        Assert.Null(result);
    }

    [Theory]
    [InlineData("admin' OR '1'='1")]
    [InlineData("' OR 1=1 --")]
    public async Task SqlInjection_InPassword_FailsGracefully(string maliciousPassword)
    {
        var auth = CreateAuth();
        await auth.RegisterAsync("Org", "user@test.com", "real-password", "User");

        var result = await auth.LoginAsync("user@test.com", maliciousPassword);
        Assert.Null(result);
    }

    // ── XSS Payload Handling ──────────────────────────────────────────

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("<img src=x onerror=alert('xss')>")]
    [InlineData("javascript:alert(1)")]
    public async Task XssPayload_InOrgName_StoredWithoutExecution(string xssPayload)
    {
        var auth = CreateAuth();
        var result = await auth.RegisterAsync(xssPayload, "xss@test.com", "password123", "User");

        // Should store the raw value (output encoding is the frontend's job)
        // The important thing is it doesn't crash or cause injection
        Assert.NotNull(result);
        Assert.Equal(xssPayload, result.Value.Org.Name);
    }

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("{{constructor.constructor('return this')()}}")]
    public async Task XssPayload_InDisplayName_StoredSafely(string xssPayload)
    {
        var auth = CreateAuth();
        var result = await auth.RegisterAsync("Safe Org", "safe@test.com", "password123", xssPayload);

        Assert.NotNull(result);
    }

    // ── Boundary Values ───────────────────────────────────────────────

    [Fact]
    public async Task EmptyEmail_Login_ReturnsNull()
    {
        var auth = CreateAuth();
        var result = await auth.LoginAsync("", "password");
        Assert.Null(result);
    }

    [Fact]
    public async Task EmptyPassword_Login_ReturnsNull()
    {
        var auth = CreateAuth();
        await auth.RegisterAsync("Org", "user@test.com", "real-password", "User");
        var result = await auth.LoginAsync("user@test.com", "");
        Assert.Null(result);
    }

    [Fact]
    public async Task VeryLongEmail_Login_DoesNotCrash()
    {
        var auth = CreateAuth();
        var longEmail = new string('a', 10000) + "@test.com";
        var result = await auth.LoginAsync(longEmail, "password");
        Assert.Null(result);
    }

    [Fact]
    public async Task VeryLongPassword_Login_DoesNotCrash()
    {
        var auth = CreateAuth();
        await auth.RegisterAsync("Org", "user@test.com", "real-password", "User");
        var result = await auth.LoginAsync("user@test.com", new string('x', 100000));
        Assert.Null(result);
    }

    // ── Unicode and Special Characters ────────────────────────────────

    [Theory]
    [InlineData("用户@测试.com")]
    [InlineData("пользователь@тест.com")]
    [InlineData("user+tag@test.com")]
    [InlineData("user.name@sub.domain.test.com")]
    public async Task SpecialCharacterEmails_HandleGracefully(string email)
    {
        var auth = CreateAuth();
        var result = await auth.RegisterAsync("Org", email, "password123", "User");

        // Should either succeed or return null, never crash
        if (result.HasValue)
            Assert.Equal(email, result.Value.User.Email);
    }

    // ── Null Byte Injection ───────────────────────────────────────────

    [Fact]
    public async Task NullByteInEmail_DoesNotBypassValidation()
    {
        var auth = CreateAuth();
        var result = await auth.LoginAsync("admin\0@test.com", "password");
        Assert.Null(result);
    }

    // ── Password Security Via Registration ───────────────────────────

    [Fact]
    public async Task SamePassword_DifferentUsers_DifferentTokens()
    {
        var auth = CreateAuth();
        var u1 = await auth.RegisterAsync("Org1", "u1@test.com", "same-password", "User1");
        var u2 = await auth.RegisterAsync("Org2", "u2@test.com", "same-password", "User2");

        Assert.NotNull(u1);
        Assert.NotNull(u2);
        // Same password should produce different tokens (different user context)
        Assert.NotEqual(u1.Value.Tokens.AccessToken, u2.Value.Tokens.AccessToken);
    }

    [Fact]
    public async Task WrongPassword_AlwaysFails()
    {
        var auth = CreateAuth();
        await auth.RegisterAsync("Org", "user@test.com", "correct-pass", "User");

        for (int i = 0; i < 10; i++)
        {
            var result = await auth.LoginAsync("user@test.com", $"wrong-{i}");
            Assert.Null(result);
        }

        // Correct password still works after many failures
        var correct = await auth.LoginAsync("user@test.com", "correct-pass");
        Assert.NotNull(correct);
    }
}
