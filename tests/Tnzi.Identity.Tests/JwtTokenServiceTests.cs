using System.IdentityModel.Tokens.Jwt;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// JwtTokenService 自定义 claim 签发测试（F1）。
/// </summary>
public class JwtTokenServiceTests
{
    private static JwtTokenService CreateService()
    {
        var identityOptions = new IdentityOptions();
        identityOptions.Jwt.SecretKey = "test-secret-key-at-least-32-chars-long-1234567890!!";
        identityOptions.Jwt.Issuer = "test-issuer";
        identityOptions.Jwt.Audience = "test-audience";
        identityOptions.Jwt.AccessTokenExpirationMinutes = 60;

        var serviceProvider = new Mock<IServiceProvider>();
        return new JwtTokenService(Microsoft.Extensions.Options.Options.Create(identityOptions), null, serviceProvider.Object, null);
    }

    private static User CreateUser() => new() { Id = Guid.NewGuid(), UserName = "testuser" };

    private static IReadOnlyList<Claim> Decode(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.ToList();

    [Fact]
    public void GenerateToken_WithExtraClaims_IncludesThemInToken()
    {
        var service = CreateService();
        var user = CreateUser();

        var token = service.GenerateToken(user, new[] { "user" },
            new[] { new Claim("ai_roles", "admin recruiter") });

        Decode(token).ShouldContain(c => c.Type == "ai_roles" && c.Value == "admin recruiter");
    }

    [Fact]
    public void GenerateToken_WithoutExtraClaims_ContainsNoCustomClaims()
    {
        var service = CreateService();
        var user = CreateUser();

        var token = service.GenerateToken(user, new[] { "user" });

        Decode(token).ShouldNotContain(c => c.Type == "ai_roles");
    }

    [Fact]
    public void GenerateToken_ExtraClaims_DoNotOverrideReservedRoleClaims()
    {
        var service = CreateService();
        var user = CreateUser();

        // Attempt to inject a privileged role via extraClaims - must be ignored.
        var token = service.GenerateToken(user, new[] { "user" },
            new[] { new Claim(ClaimTypes.Role, "superadmin") });

        var roleValues = Decode(token)
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role" || c.Type == "roles")
            .Select(c => c.Value)
            .ToList();
        roleValues.ShouldContain("user");
        roleValues.ShouldNotContain("superadmin");
    }

    [Theory]
    [InlineData("role")]
    [InlineData("nameid")]
    [InlineData("sub")]
    [InlineData("unique_name")]
    public void GenerateToken_ExtraClaims_RejectShortNameReservedClaims(string reservedShortName)
    {
        var service = CreateService();
        var user = CreateUser();
        var injected = Guid.NewGuid().ToString();

        // JWT short-name forms of reserved claims must be filtered too - otherwise
        // a caller injects `role=superadmin` / `nameid=<victim>` via extraClaims and
        // the validating side (MapInboundClaims) maps them back to ClaimTypes.* →
        // privilege escalation / identity spoofing.
        var token = service.GenerateToken(user, new[] { "user" },
            new[] { new Claim(reservedShortName, injected) });

        var injectedValues = Decode(token)
            .Where(c => c.Type == reservedShortName)
            .Select(c => c.Value)
            .ToList();
        injectedValues.ShouldNotContain(injected);
    }

    [Fact]
    public void GenerateTokenResult_WithExtraClaims_AccessTokenContainsThem()
    {
        var service = CreateService();
        var user = CreateUser();

        var result = service.GenerateTokenResult(user, new[] { "user" },
            new[] { new Claim("ai_roles", "admin") });

        Decode(result.AccessToken).ShouldContain(c => c.Type == "ai_roles" && c.Value == "admin");
    }

    [Fact]
    public void GenerateToken_WithSessionId_IncludesSessionIdClaim()
    {
        var service = CreateService();
        var user = CreateUser();
        var sessionId = Guid.NewGuid();

        var token = service.GenerateToken(user, new[] { "user" }, sessionId: sessionId);

        Decode(token).ShouldContain(c =>
            c.Type == IdentityConstants.ClaimTypeNames.SessionId && c.Value == sessionId.ToString());
    }

    [Fact]
    public void GenerateToken_WithEmptySessionId_HasNoSessionIdClaim()
    {
        var service = CreateService();
        var user = CreateUser();

        var token = service.GenerateToken(user, new[] { "user" }, sessionId: Guid.Empty);

        Decode(token).ShouldNotContain(c => c.Type == IdentityConstants.ClaimTypeNames.SessionId);
    }

    [Fact]
    public void GenerateToken_ExtraClaims_CannotForgeSessionId()
    {
        var service = CreateService();
        var user = CreateUser();
        var realSession = Guid.NewGuid();
        var forgedSession = Guid.NewGuid().ToString();

        // A caller must not be able to bind the token to an arbitrary session via
        // extraClaims - session_id is a reserved, framework-set claim.
        var token = service.GenerateToken(user, new[] { "user" },
            new[] { new Claim(IdentityConstants.ClaimTypeNames.SessionId, forgedSession) },
            sessionId: realSession);

        var sessionValues = Decode(token)
            .Where(c => c.Type == IdentityConstants.ClaimTypeNames.SessionId)
            .Select(c => c.Value)
            .ToList();
        sessionValues.ShouldContain(realSession.ToString());
        sessionValues.ShouldNotContain(forgedSession);
    }
}
