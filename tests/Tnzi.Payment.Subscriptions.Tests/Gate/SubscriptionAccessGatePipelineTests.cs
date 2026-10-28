using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Tnzi.AspNetCore.Options;
using Tnzi.Caching;

namespace Tnzi.Payment.Subscriptions.Tests.Gate;

/// <summary>
/// 访问门禁走一条真实的 ASP.NET Core 管线：认证 → 授权 → 门禁 → 端点，SignalR 客户端经同一个 TestServer 连上真实的 Hub。
/// </summary>
/// <remarks>
/// 判定服务、缓存、中间件、Hub 过滤器都是真的；只有「这个用户代表哪个账户」（解析器）、「账户此刻什么境况」（订阅服务）
/// 与「谁是超管」（功能授权服务）是替身：它们各自有自己的集成测试，这里要钉住的是门禁本身的放行与拒绝规则。
/// </remarks>
public sealed class SubscriptionAccessGatePipelineTests : IAsyncLifetime
{
    private const string UserHeader = "X-Test-User";
    private const string BypassCode = "support.access";

    private static readonly Guid SuspendedManager = Guid.NewGuid();
    private static readonly Guid SuspendedMember = Guid.NewGuid();
    private static readonly Guid GoodUser = Guid.NewGuid();
    private static readonly Guid SuperAdmin = Guid.NewGuid();
    private static readonly Guid BypassHolder = Guid.NewGuid();
    private static readonly Guid NoAccountUser = Guid.NewGuid();

    private static readonly BillingAccountRef FirmAccount = new(BillingAccountTypes.Installation, "default");
    private static readonly BillingAccountRef OtherAccount = new(BillingAccountTypes.Installation, "other");

    private readonly Dictionary<string, BillingStanding> _standings = new()
    {
        [FirmAccount.Id] = BillingStanding.Suspended,
        [OtherAccount.Id] = BillingStanding.Good
    };

    private WebApplication? _app;
    private SubscriptionAccessGateOptions _gate = new() { Enabled = true, CacheSeconds = 0, AllowedPaths = ["firm/contact", "/hubs/notices"], BypassPermissions = [BypassCode] };
    private int _standingQueries;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_app != null)
            await _app.DisposeAsync();
    }

    [Fact]
    public async Task GateOff_ByDefault_NothingIsRefused()
    {
        _gate = new SubscriptionAccessGateOptions();
        var client = await StartAsync();

        var response = await GetAsync(client, "/api/firm/data", SuspendedMember);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _standingQueries.ShouldBe(0); // 关着的门禁连境况都不查
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuspendedAccount_Answers402_WithCanManage(bool manager)
    {
        var client = await StartAsync();

        var response = await GetAsync(client, "/api/firm/data", manager ? SuspendedManager : SuspendedMember);

        response.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetInt32().ShouldBe(402);
        body.RootElement.GetProperty("errorCode").GetString().ShouldBe(ErrorCodes.SubscriptionSuspended);
        var details = body.RootElement.GetProperty("errorDetails");
        details.GetProperty("standing").GetString().ShouldBe("Suspended");
        details.GetProperty("canManage").GetBoolean().ShouldBe(manager);
    }

    [Fact]
    public async Task GoodAccount_AndUserWithoutAccount_PassThrough()
    {
        var client = await StartAsync();

        (await GetAsync(client, "/api/firm/data", GoodUser)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/api/firm/data", NoAccountUser)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SuperAdmin_IsNeverGated()
    {
        var client = await StartAsync();

        (await GetAsync(client, "/api/firm/data", SuperAdmin)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BypassPermissionHolder_IsNotGated()
    {
        var client = await StartAsync();

        (await GetAsync(client, "/api/firm/data", BypassHolder)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/users/profile/sessions")] // 内置：会话管理
    [InlineData("/api/auth/logout")]            // 内置：退出
    [InlineData("/api/subscriptions/standing")] // 内置：外壳读境况
    [InlineData("/api/firm/contact")]           // 配置：相对 API 前缀
    [InlineData("/api/firm/notice")]            // [AllowWhenSuspended]
    [InlineData("/api/public")]                 // [AllowAnonymous]，带着暂停账户的身份调
    public async Task AllowList_StaysOpenForSuspendedAccount(string path)
    {
        var client = await StartAsync();

        (await GetAsync(client, path, SuspendedMember)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AllowedPaths_MatchWholeSegments()
    {
        var client = await StartAsync();

        // firm/contact 放行的是 /api/firm/contact 及其下级，不是名字恰好以它开头的另一个端点
        (await GetAsync(client, "/api/firm/contacts", SuspendedMember)).StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task Unauthenticated_GetsTheUsual401_Not402()
    {
        var client = await StartAsync();

        (await client.GetAsync("/api/firm/data")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Recovery_TakesEffectAsSoonAsTheStandingEventInvalidatesTheCache()
    {
        _gate = new SubscriptionAccessGateOptions { Enabled = true, CacheSeconds = 300 };
        var client = await StartAsync();

        (await GetAsync(client, "/api/firm/data", SuspendedMember)).StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);

        // 补缴成功：订阅表已经是 Good，但门禁缓存着 Suspended
        _standings[FirmAccount.Id] = BillingStanding.Good;
        (await GetAsync(client, "/api/firm/data", SuspendedMember)).StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);

        await PublishStandingChangedAsync(FirmAccount);

        (await GetAsync(client, "/api/firm/data", SuspendedMember)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Hub_SuspendedAccountCannotConnect()
    {
        var client = await StartAsync();

        var connection = Connect(SuspendedMember);
        await Should.ThrowAsync<Exception>(() => connection.StartAsync());
        connection.State.ShouldBe(HubConnectionState.Disconnected);
    }

    [Fact]
    public async Task Hub_SuperAdminConnectsAndInvokes()
    {
        await StartAsync();

        await using var connection = Connect(SuperAdmin);
        await connection.StartAsync();

        (await connection.InvokeAsync<string>(nameof(GateTestHub.Ping))).ShouldBe("pong");
    }

    [Fact]
    public async Task Hub_WebSocketOpenedBeforeSuspension_IsRefusedOnTheNextInvocation()
    {
        _standings[FirmAccount.Id] = BillingStanding.Good;
        await StartAsync();

        await using var connection = Connect(SuspendedMember, HttpTransportType.WebSockets);
        await connection.StartAsync();
        (await connection.InvokeAsync<string>(nameof(GateTestHub.Ping))).ShouldBe("pong");

        // WebSocket 建好之后，方法调用不再经过任何 HTTP 请求：只有 Hub 过滤器拦得住
        _standings[FirmAccount.Id] = BillingStanding.Suspended;

        var error = await Should.ThrowAsync<HubException>(() => connection.InvokeAsync<string>(nameof(GateTestHub.Ping)));
        error.Message.ShouldContain(ErrorCodes.SubscriptionSuspended);
    }

    [Fact]
    public async Task Hub_LongPollingOpenedBeforeSuspension_IsRefusedByTheHttpGate()
    {
        _standings[FirmAccount.Id] = BillingStanding.Good;
        await StartAsync();

        await using var connection = Connect(SuspendedMember);
        await connection.StartAsync();
        (await connection.InvokeAsync<string>(nameof(GateTestHub.Ping))).ShouldBe("pong");

        _standings[FirmAccount.Id] = BillingStanding.Suspended;

        // 长轮询的每一次发送都是一个 HTTP 请求，中间件就地答 402
        var error = await Should.ThrowAsync<HttpRequestException>(() => connection.InvokeAsync<string>(nameof(GateTestHub.Ping)));
        error.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
    }

    private async Task<HttpClient> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var services = builder.Services;
        services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        services.AddAuthorization();
        services.AddControllers().AddApplicationPart(typeof(GateTestController).Assembly);
        // 与模块同一条登记方式：全局 Hub 过滤器
        services.AddSignalR();
        services.Configure<HubOptions>(options => options.AddFilter<SubscriptionAccessGateHubFilter>());

        services.AddSingleton<IOptionsMonitor<SubscriptionAccessGateOptions>>(new StaticOptionsMonitor<SubscriptionAccessGateOptions>(_gate));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new AspNetCoreOptions()));
        services.AddMemoryCache();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new Tnzi.Caching.CachingOptions()));
        services.AddSingleton<ICache, MemoryCacheService>();
        services.AddSingleton<SubscriptionStandingCache>();
        services.AddScoped<BillingStandingCacheInvalidationHandler>();
        services.AddScoped<ISubscriptionAccessGate, SubscriptionAccessGate>();
        services.AddSingleton(CreateResolver());
        services.AddSingleton(CreateSubscriptionService());
        services.AddSingleton(CreateFunctionAuthorization());
        services.AddSingleton(CreatePermissionChecker());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        // 与模块同一个位置：授权之后、端点执行之前
        app.UseMiddleware<SubscriptionAccessGateMiddleware>();
        app.MapControllers();
        app.MapHub<GateTestHub>("/hubs/test");

        await app.StartAsync();
        _app = app;
        return app.GetTestServer().CreateClient();
    }

    private HubConnection Connect(Guid userId, HttpTransportType transport = HttpTransportType.LongPolling)
    {
        var server = _app!.GetTestServer();
        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/test"), options =>
            {
                options.Transports = transport;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Headers[UserHeader] = userId.ToString();
                if (transport == HttpTransportType.WebSockets)
                {
                    options.SkipNegotiation = true;
                    options.WebSocketFactory = async (context, cancellationToken) =>
                    {
                        var client = server.CreateWebSocketClient();
                        client.ConfigureRequest = request => request.Headers[UserHeader] = userId.ToString();
                        return await client.ConnectAsync(context.Uri, cancellationToken);
                    };
                }
            })
            .Build();
    }

    private async Task PublishStandingChangedAsync(BillingAccountRef account)
    {
        using var scope = _app!.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<BillingStandingCacheInvalidationHandler>();
        await handler.HandleAsync(new BillingAccountStandingChangedEvent
        {
            BillingAccountType = account.Type,
            BillingAccountId = account.Id,
            PreviousStanding = BillingStanding.Suspended,
            CurrentStanding = BillingStanding.Good
        });
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string path, Guid userId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(UserHeader, userId.ToString());
        return client.SendAsync(request);
    }

    private static IBillingAccountResolver CreateResolver()
    {
        var resolver = new Mock<IBillingAccountResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid userId, CancellationToken _) =>
            {
                if (userId == NoAccountUser)
                    return null;
                if (userId == GoodUser)
                    return new BillingAccountAccess(OtherAccount, true, true);
                return new BillingAccountAccess(FirmAccount, true, userId == SuspendedManager);
            });
        return resolver.Object;
    }

    private ISubscriptionService CreateSubscriptionService()
    {
        var service = new Mock<ISubscriptionService>();
        service.Setup(s => s.GetStandingAsync(It.IsAny<BillingAccountRef>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingAccountRef account, string? _, CancellationToken _) =>
            {
                Interlocked.Increment(ref _standingQueries);
                return _standings[account.Id];
            });
        return service.Object;
    }

    private static IFunctionAuthorizationService CreateFunctionAuthorization()
    {
        var authorization = new Mock<IFunctionAuthorizationService>();
        authorization.Setup(a => a.IsSuperAdminAsync(It.IsAny<Guid>())).ReturnsAsync((Guid userId) => userId == SuperAdmin);
        return authorization.Object;
    }

    private static IPermissionChecker CreatePermissionChecker()
    {
        var checker = new Mock<IPermissionChecker>();
        checker.Setup(c => c.IsGrantedAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync((Guid userId, string code) => userId == BypassHolder && code == BypassCode);
        return checker.Object;
    }

    /// <summary>按请求头认人：带头就是那个用户，不带就是匿名。</summary>
    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "GateTest";

        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var value))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, value.ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

/// <summary>门禁测试用的端点：路由覆盖内置放行路径、配置放行路径、特性放行与普通业务端点。</summary>
[ApiController]
[Authorize]
public sealed class GateTestController : ControllerBase
{
    [HttpGet("api/firm/data")] public string Data() => "data";
    [HttpGet("api/firm/contact")] public string Contact() => "contact";
    [HttpGet("api/firm/contacts")] public string Contacts() => "contacts";
    [HttpGet("api/firm/notice"), AllowWhenSuspended] public string Notice() => "notice";
    [HttpGet("api/users/profile/sessions")] public string Sessions() => "sessions";
    [HttpGet("api/auth/logout")] public string Logout() => "logout";
    [HttpGet("api/subscriptions/standing")] public string Standing() => "standing";
    [HttpGet("api/public"), AllowAnonymous] public string Public() => "public";
}

/// <summary>门禁测试用的 Hub。</summary>
[Authorize]
public sealed class GateTestHub : Hub
{
    public string Ping() => "pong";
}
