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

    /// <summary>没有配置 SecretKey 时，按给定环境名构造服务。</summary>
    private static JwtTokenService CreateServiceWithoutSecret(string? environmentName)
    {
        var identityOptions = new IdentityOptions();
        identityOptions.Jwt.SecretKey = null!;

        // 完全限定以消歧义：Microsoft.Extensions.Hosting 下有同名接口，
        // 而 JwtTokenService 的构造参数是 AspNetCore 那个。
        Microsoft.AspNetCore.Hosting.IWebHostEnvironment? environment = null;
        if (environmentName is not null)
        {
            var env = new Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
            env.SetupGet(x => x.EnvironmentName).Returns(environmentName);
            environment = env.Object;
        }

        // Development 分支会 LogWarning，而 ApplicationService 的 logger 是
        // GetRequiredService<ILoggerFactory> 拿的 —— 裸 Mock 会在那里抛。
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(x => x.GetService(typeof(Microsoft.Extensions.Logging.ILoggerFactory)))
            .Returns(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        return new JwtTokenService(
            Microsoft.Extensions.Options.Options.Create(identityOptions),
            null,
            serviceProvider.Object,
            environment);
    }

    /// <summary>
    /// 缺少 SecretKey 时，只有 Development 允许回落到内置默认密钥。
    /// </summary>
    /// <remarks>
    /// 判据曾是 <c>EnvironmentName == "Production"</c>，有两个洞：大小写敏感
    /// （<c>production</c> 直接绕过），以及 Staging 和任何自定义环境名都不在拦截范围内 ——
    /// 而它们同样是真实部署。默认密钥是写在源码里的公开字符串，一旦签了真 token，
    /// 任何人都能伪造身份。这组用例把「哪些环境会被拒绝」钉死。
    /// </remarks>
    [Theory]
    [InlineData("Production")]
    [InlineData("production")]   // 大小写敏感的旧判据在这里失守
    [InlineData("PRODUCTION")]
    [InlineData("Staging")]
    [InlineData("Prod")]         // 自定义环境名
    [InlineData("prod-eu")]
    [InlineData(null)]           // 没有 IWebHostEnvironment：未知环境按拒绝处理
    public void Constructor_WithoutSecretKey_OutsideDevelopment_Throws(string? environmentName)
    {
        var ex = Should.Throw<InvalidOperationException>(() => CreateServiceWithoutSecret(environmentName));
        ex.Message.ShouldContain("JWT SecretKey must be configured");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    public void Constructor_WithoutSecretKey_InDevelopment_FallsBackToDefault(string environmentName)
    {
        var service = CreateServiceWithoutSecret(environmentName);

        // 能签出 token 就说明回落生效了（开发便利是这个分支存在的唯一理由）。
        var token = service.GenerateToken(CreateUser(), []);
        token.ShouldNotBeNullOrWhiteSpace();
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
