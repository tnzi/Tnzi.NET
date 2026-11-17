using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tnzi.Data;
using Tnzi.Hosting;
using Tnzi.Identity.Entities;
using Tnzi.Identity.Services;
using Tnzi.Modules;
using Tnzi.MultiTenancy;
using Tnzi.Security.Claims;

namespace Tnzi.Identity.OpenIdConnect.Tests.Integration;

/// <summary>
/// 一个真实的 ASP.NET Core 宿主（TestServer + 临时 SQLite 文件），加载 Identity 与 OpenID Connect 提供方，
/// 再扮演依赖方（RP）发 HTTP 请求。宿主按类共享，每条用例自建用户与客户端，互不干扰。
/// </summary>
/// <remarks>
/// 密钥是测试时现生成的 RSA 私钥（PEM）与 256 位 AES 密钥，走的是生产路径（配置里的密钥环），不是开发环境的临时密钥。
/// 依赖方地址用 https：OpenIddict 默认拒绝明文请求，测试保持这条要求开着。
/// </remarks>
public class OidcProviderHost : IAsyncLifetime
{
    /// <summary>签发方。</summary>
    public const string Issuer = "https://localhost/";

    /// <summary>平台的「继续授权」页（消费方 SPA）。</summary>
    public const string LoginUrl = "https://platform.example.test/oidc/continue";

    /// <summary>平台的同意页（消费方 SPA）。</summary>
    public const string ConsentUrl = "https://platform.example.test/oidc/consent";

    /// <summary>依赖方的回调地址。</summary>
    public const string RedirectUri = "https://rp.example.test/signin-oidc";

    /// <summary>依赖方的登出后回跳地址。</summary>
    public const string PostLogoutRedirectUri = "https://rp.example.test/signed-out";

    /// <summary>所有请求带的 User-Agent（平台会话按它做绑定校验）。</summary>
    public const string UserAgent = "OidcRelyingPartyTest/1.0";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "tnzi-oidc-" + Guid.NewGuid().ToString("N") + ".db");
    private WebApplication? _app;

    /// <summary>额外配置（子类夹具改开关用）。</summary>
    protected virtual IDictionary<string, string?> ExtraConfiguration => new Dictionary<string, string?>();

    /// <summary>宿主的服务。</summary>
    public IServiceProvider Services => _app!.Services;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ApplicationName = typeof(OidcProviderHost).Assembly.GetName().Name,
        });
        builder.WebHost.UseTestServer();

        using var rsa = RSA.Create(2048);
        var settings = new Dictionary<string, string?>
        {
            ["Database:DbContexts:0:Name"] = "Default",
            ["Database:DbContexts:0:DbContextType"] = typeof(OidcTestDbContext).AssemblyQualifiedName,
            ["Database:DbContexts:0:ConnectionString"] = $"Data Source={_databasePath}",
            ["Database:DbContexts:0:Provider"] = "Sqlite",
            ["AspNetCore:EnableForwardedHeaders"] = "false",
            ["Identity:OpenIdConnect:Issuer"] = Issuer,
            ["Identity:OpenIdConnect:LoginUrl"] = LoginUrl,
            ["Identity:OpenIdConnect:ConsentUrl"] = ConsentUrl,
            ["Identity:OpenIdConnect:SigningKeys:test-rsa"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["Identity:OpenIdConnect:EncryptionKeys:test-aes"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };
        foreach (var (key, value) in ExtraConfiguration)
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);

        _app = await TnziApp.CreateAsync<OidcTestStartupModule>(builder);
        await _app.StartAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_app != null)
        {
            await _app.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // 临时目录里的残留文件不影响结论
            }
        }
    }

    /// <summary>一个不跟随重定向的 https 客户端。</summary>
    public HttpClient CreateClient()
    {
        var client = _app!.GetTestServer().CreateClient();
        client.BaseAddress = new Uri(Issuer);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    /// <summary>建一个用户和一个平台会话，返回会话绑定的平台访问令牌。</summary>
    public async Task<PlatformLogin> SignInPlatformUserAsync(bool emailConfirmed = true)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var name = "oidc-" + Guid.NewGuid().ToString("N")[..10];
        var user = new User { UserName = name, Email = name + "@example.test", EmailConfirmed = emailConfirmed };
        var created = await userManager.CreateAsync(user, "Oidc-Test-Passw0rd!");
        created.Succeeded.ShouldBeTrue(string.Join("; ", created.Errors.Select(e => e.Description)));

        var sessionId = await scope.ServiceProvider.GetRequiredService<ISessionService>()
            .CreateSessionAsync(user.Id, "test", ipAddress: null, UserAgent);
        var token = scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateToken(user, [], null, sessionId);
        return new PlatformLogin(user.Id, user.UserName!, user.Email!, sessionId, token);
    }

    /// <summary>在一个新的 DI 作用域里调用服务。</summary>
    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    /// <summary>建立会话桥，返回可以放进 <c>Cookie</c> 请求头的 <c>name=value</c>。</summary>
    public async Task<string> EstablishBridgeAsync(HttpClient client, PlatformLogin login)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/session");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());

        var setCookie = response.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("tnzi_oidc=", StringComparison.Ordinal));
        setCookie.ShouldContain("httponly", Case.Insensitive);
        setCookie.ShouldContain("secure", Case.Insensitive);
        setCookie.ShouldContain("samesite=lax", Case.Insensitive);
        setCookie.ShouldContain("path=/connect", Case.Insensitive);
        return setCookie.Split(';')[0];
    }

    /// <summary>发一个 GET，可选带 cookie。</summary>
    public static async Task<HttpResponseMessage> GetAsync(HttpClient client, string url, string? cookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (cookie != null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        return await client.SendAsync(request);
    }

    /// <summary>读重定向目标的查询参数。</summary>
    public static Dictionary<string, string> RedirectQuery(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, $"expected a redirect, got {(int)response.StatusCode}");
        var location = response.Headers.Location!;
        location.IsAbsoluteUri.ShouldBeTrue();
        return QueryHelpers.ParseQuery(location.Query).ToDictionary(p => p.Key, p => p.Value.ToString());
    }

    /// <summary>读 JSON 响应体。</summary>
    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}

/// <summary>一个已登录平台的用户。</summary>
public sealed record PlatformLogin(Guid UserId, string UserName, string Email, Guid SessionId, string AccessToken);

/// <summary>
/// 测试宿主的启动模块：Identity + OpenID Connect 提供方。
/// </summary>
[DependsOn(typeof(Tnzi.EFCore.EFCoreModule), typeof(IdentityModule), typeof(Tnzi.Authorization.AuthorizationModule), typeof(IdentityOpenIdConnectModule))]
public sealed class OidcTestStartupModule : HostingModule;

/// <summary>测试用的主 DbContext（带 Identity，框架实体与本模块实体经自动发现注册）。</summary>
public class OidcTestDbContext : Tnzi.Identity.Data.IdentityDbContext<OidcTestDbContext>
{
    /// <inheritdoc />
    public OidcTestDbContext(
        DbContextOptions<OidcTestDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant = null,
        IDataFilterManager? dataFilterManager = null,
        TimeProvider? timeProvider = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(options, currentUser, currentTenant, dataFilterManager, timeProvider, multiTenancyOptions)
    {
    }
}
