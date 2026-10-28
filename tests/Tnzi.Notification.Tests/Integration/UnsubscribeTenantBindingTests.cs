using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Tnzi.AspNetCore.Models;
using Tnzi.MultiTenancy;
using Tnzi.Notification.Controllers;
using Tnzi.Security.Claims;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 一键退订落在签发令牌的那个租户里，不管兑现它的匿名请求带着什么租户上下文。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：令牌只签了「地址 | 渠道 | 分类」，不带租户。退订端点是匿名的，
/// Gmail / Outlook 的 RFC 8058 一键退订直接 POST 到全局配置的那一个地址，请求里没有任何租户信息，
/// 于是退订行以宿主身份落库（<c>TenantId = NULL</c>）。租户 A 下一次群发在 A 的作用域里判抑制，
/// 严格等值的租户过滤器看不见宿主行，照发不误；A 的管理员在抑制名单里也看不见它。
/// 接口回 200、落地页说「已退订」，合规要求的一键退订对每个租户都静默失效。
/// </para>
/// <para>
/// 令牌现在把签发时的租户签进去，兑现时切到那个租户。租户来自签名，请求头选不了它。
/// </para>
/// </remarks>
public class UnsubscribeTenantBindingTests : IDisposable
{
    private const string Secret = "unsubscribe-tenant-binding-secret";
    private const string Address = "reader@example.com";

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly SqliteConnection _connection;

    public UnsubscribeTenantBindingTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var provider = BuildProvider(userTenantId: null);
        provider.GetRequiredService<NotificationTestDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task AOneClickUnsubscribe_FromAnAnonymousRequest_LandsInTheTenantThatSentTheMail()
    {
        var token = MintIn(TenantA, "marketing");

        var result = await RedeemAnonymouslyAsync(c => c.OneClick(token, CancellationToken.None));

        result.Succeeded.ShouldBeTrue(result.Message);
        (await OptOutRowsAsync()).ShouldHaveSingleItem().TenantId.ShouldBe(TenantA);
        (await AllowedInAsync(TenantA, "marketing")).ShouldBeEmpty("tenant A's next campaign must skip the recipient");
    }

    [Fact]
    public async Task TheLandingPageUnsubscribe_LandsInThatTenantToo()
    {
        var token = MintIn(TenantA, category: null);

        var result = await RedeemAnonymouslyAsync(c => c.Unsubscribe(new UnsubscribeRequestDto { Token = token }, CancellationToken.None));

        result.Succeeded.ShouldBeTrue(result.Message);
        (await OptOutRowsAsync()).ShouldHaveSingleItem().TenantId.ShouldBe(TenantA);
    }

    /// <summary>
    /// 匿名请求可以带租户头（或查询串、Cookie），解析中间件会照单全收。
    /// 令牌签的是 A，兑现时请求说自己是 B：行仍落在 A，B 里什么都没有；
    /// 否则拿到一封信的人就能把收件人在任意租户里退订掉。
    /// </summary>
    [Fact]
    public async Task ARequestClaimingAnotherTenant_CannotRedirectTheToken()
    {
        var token = MintIn(TenantA, "marketing");

        var result = await RedeemAnonymouslyAsync(c => c.OneClick(token, CancellationToken.None), ambientTenant: TenantB);

        result.Succeeded.ShouldBeTrue(result.Message);
        var row = (await OptOutRowsAsync()).ShouldHaveSingleItem();
        row.TenantId.ShouldBe(TenantA);
        (await AllowedInAsync(TenantB, "marketing")).ShouldBe([Address]);
    }

    [Fact]
    public async Task Resubscribing_RemovesTheRowFromTheTokensTenant()
    {
        var token = MintIn(TenantA, "marketing");
        await RedeemAnonymouslyAsync(c => c.OneClick(token, CancellationToken.None));

        var result = await RedeemAnonymouslyAsync(c => c.Resubscribe(new UnsubscribeRequestDto { Token = token }, CancellationToken.None));

        result.Succeeded.ShouldBeTrue(result.Message);
        (await OptOutRowsAsync()).ShouldBeEmpty();
        (await AllowedInAsync(TenantA, "marketing")).ShouldBe([Address]);
    }

    /// <summary>
    /// 签名覆盖租户：把载荷里的租户换成 B、保留原签名，令牌就不再成立。
    /// </summary>
    [Fact]
    public void SwappingTheTenantInsideAToken_BreaksItsSignature()
    {
        var token = MintIn(TenantA, "marketing");
        var payload = Encoding.UTF8.GetString(Base64UrlDecode(token[..token.IndexOf('.')]));
        payload.ShouldContain(TenantA.ToString("N"));

        var forged = Base64UrlEncode(Encoding.UTF8.GetBytes(payload.Replace(TenantA.ToString("N"), TenantB.ToString("N"))))
                     + token[token.IndexOf('.')..];

        using var provider = BuildProvider(userTenantId: null);
        provider.GetRequiredService<INotificationOptOutService>().ResolveUnsubscribeToken(forged).ShouldBeNull();
    }

    /// <summary>
    /// 修复之前签发的令牌只有三段、不带租户，它们还躺在收件箱里：照常成立，按宿主兑现（与当初的落点一致），
    /// 且同样不受请求头左右。
    /// </summary>
    [Fact]
    public async Task ALegacyThreePartToken_StillWorks_AsAHostOptOut()
    {
        var legacy = SignLegacy($"{Address}|{(int)NotificationType.Email}|marketing");

        using (var provider = BuildProvider(userTenantId: null))
        {
            var payload = provider.GetRequiredService<INotificationOptOutService>().ResolveUnsubscribeToken(legacy);
            payload.ShouldNotBeNull();
            payload!.TenantId.ShouldBeNull();
            payload.Address.ShouldBe(Address);
        }

        var result = await RedeemAnonymouslyAsync(c => c.OneClick(legacy, CancellationToken.None), ambientTenant: TenantB);

        result.Succeeded.ShouldBeTrue(result.Message);
        (await OptOutRowsAsync()).ShouldHaveSingleItem().TenantId.ShouldBeNull();
    }

    private string MintIn(Guid tenantId, string? category)
    {
        using var provider = BuildProvider(userTenantId: tenantId);
        return provider.GetRequiredService<INotificationOptOutService>().CreateUnsubscribeToken(Address, NotificationType.Email, category);
    }

    /// <summary>以匿名请求兑现令牌：没有登录用户；<paramref name="ambientTenant"/> 模拟租户解析中间件按请求头切过去的租户。</summary>
    private async Task<ApiResult> RedeemAnonymouslyAsync(Func<DefaultUnsubscribeController, Task<ApiResult>> call, Guid? ambientTenant = null)
    {
        using var provider = BuildProvider(userTenantId: null, authenticated: false);
        using var scope = provider.CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using (ambientTenant.HasValue ? currentTenant.Change(ambientTenant) : null)
        {
            var controller = new DefaultUnsubscribeController(scope.ServiceProvider.GetRequiredService<INotificationOptOutService>());
            return await call(controller);
        }
    }

    private async Task<List<OptOut>> OptOutRowsAsync()
    {
        using var provider = BuildProvider(userTenantId: null);
        return await provider.GetRequiredService<NotificationTestDbContext>().OptOuts.IgnoreQueryFilters().AsNoTracking().ToListAsync();
    }

    private async Task<IReadOnlyList<string>> AllowedInAsync(Guid tenantId, string? category)
    {
        using var provider = BuildProvider(userTenantId: tenantId);
        return await provider.GetRequiredService<INotificationOptOutService>()
            .FilterAllowedAsync([Address], NotificationType.Email, category);
    }

    private ServiceProvider BuildProvider(Guid? userTenantId, bool authenticated = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.Id).Returns(authenticated ? TestHelper.DefaultTestUserId : (Guid?)null);
        currentUser.Setup(u => u.IsAuthenticated).Returns(authenticated);
        currentUser.Setup(u => u.TenantId).Returns(userTenantId);
        services.AddScoped(_ => currentUser.Object);
        // 与生产同一个实现：Change() 的覆盖存放在静态 AsyncLocal 上，DbContext 与服务读到同一份
        services.AddScoped<ICurrentTenant, CurrentTenant>();

        services.AddDbContext<NotificationTestDbContext>(options =>
        {
            options.UseSqlite(_connection);
            options.UseTnziMultiTenancy(true);
        });
        services.AddScoped<IRepository<OptOut, Guid>>(sp =>
            new EFCoreRepository<NotificationTestDbContext, OptOut, Guid>(sp.GetRequiredService<NotificationTestDbContext>(), serviceProvider: sp));

        var options = new Mock<IOptionsMonitor<NotificationOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new NotificationOptions { OptOut = new OptOutOptions { TokenSecret = Secret } });
        services.AddSingleton(options.Object);
        services.AddScoped<INotificationOptOutService, NotificationOptOutService>();

        return services.BuildServiceProvider();
    }

    /// <summary>按修复之前的格式（地址 | 渠道 | 分类）签一枚令牌：与服务同一把密钥、同一种 HMAC。</summary>
    private static string SignLegacy(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return $"{Base64UrlEncode(Encoding.UTF8.GetBytes(payload))}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
