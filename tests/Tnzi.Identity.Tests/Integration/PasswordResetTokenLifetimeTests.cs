using Microsoft.AspNetCore.DataProtection;
using TnziIdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests.Integration;

/// <summary>
/// 密码重置令牌的有效期取 <c>Identity:Recovery:ResetTokenExpirationMinutes</c>，且只有重置令牌受它管。
/// 经真实的 <c>AddTnziIdentity</c> 注册与 <see cref="UserManager{TUser}"/> 校验。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 此前这个设置只被邮件文案读：令牌走 ASP.NET Identity 的默认提供者，有效期 1 天。
/// 设置中心里调成 15 分钟、邮件里也写着 15 分钟，一封 20 小时前的重置邮件照样能改密码。
/// </para>
/// <para>
/// 令牌提供者没有可注入的时钟，所以「一枚 N 分钟前签发的令牌」按它的载荷格式（签发时刻、用户、用途、安全戳）
/// 用同一个数据保护器直接造出来。格式若在框架升级时变了，这里会变红而不是安静通过。
/// </para>
/// </remarks>
public class PasswordResetTokenLifetimeTests : IntegratedTestBase<IdentityTestDbContext>
{
    private int _resetMinutes = 30;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddSingleton<IOptionsMonitor<TnziIdentityOptions>>(new LiveOptionsMonitor(() =>
        {
            var options = new TnziIdentityOptions();
            options.Recovery.ResetTokenExpirationMinutes = _resetMinutes;
            return options;
        }));
        services.AddTnziIdentity<IdentityTestDbContext>(new ConfigurationBuilder().Build());
    }

    private static User NewUser() => new()
    {
        Id = Guid.NewGuid(),
        UserName = "reset-user",
        Email = "reset-user@example.com",
        SecurityStamp = Guid.NewGuid().ToString(),
    };

    /// <summary>每次一个新作用域：提供者随 UserManager 按请求新建，运行时设置在这一刻读。</summary>
    private (UserManager<User> Manager, DataProtectorTokenProvider<User> Provider, string ProviderName) ResolveResetProvider(IServiceScope scope)
    {
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var name = manager.Options.Tokens.PasswordResetTokenProvider;
        var provider = (DataProtectorTokenProvider<User>)scope.ServiceProvider.GetRequiredService(
            manager.Options.Tokens.ProviderMap[name].ProviderType);
        return (manager, provider, name);
    }

    /// <summary>按提供者的载荷格式、用它同名的数据保护器造一枚在 <paramref name="issuedAt"/> 签发的令牌。</summary>
    private string MintToken(DataProtectorTokenProvider<User> provider, User user, string purpose, DateTimeOffset issuedAt)
    {
        var protector = ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector(provider.Name);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), leaveOpen: true))
        {
            writer.Write(issuedAt.UtcTicks);
            writer.Write(user.Id.ToString());
            writer.Write(purpose);
            writer.Write(user.SecurityStamp ?? string.Empty);
        }

        return Convert.ToBase64String(protector.Protect(stream.ToArray()));
    }

    private async Task<bool> ResetTokenIsValidAsync(User user, TimeSpan age)
    {
        using var scope = ServiceProvider.CreateScope();
        var (manager, provider, name) = ResolveResetProvider(scope);
        var token = MintToken(provider, user, UserManager<User>.ResetPasswordTokenPurpose, DateTimeOffset.UtcNow - age);
        return await manager.VerifyUserTokenAsync(user, name, UserManager<User>.ResetPasswordTokenPurpose, token);
    }

    [Fact]
    public async Task A_reset_link_older_than_the_configured_window_is_rejected()
    {
        _resetMinutes = 30;

        (await ResetTokenIsValidAsync(NewUser(), TimeSpan.FromHours(2))).ShouldBeFalse(
            "a two-hour-old reset link must not work when the window is 30 minutes");
    }

    [Fact]
    public async Task A_reset_link_inside_the_configured_window_is_accepted()
    {
        _resetMinutes = 30;
        var user = NewUser();

        (await ResetTokenIsValidAsync(user, TimeSpan.FromMinutes(29))).ShouldBeTrue();

        using var scope = ServiceProvider.CreateScope();
        var (manager, _, name) = ResolveResetProvider(scope);
        var fresh = await manager.GeneratePasswordResetTokenAsync(user);
        (await manager.VerifyUserTokenAsync(user, name, UserManager<User>.ResetPasswordTokenPurpose, fresh)).ShouldBeTrue();
    }

    /// <summary>运行时设置：改了之后下一个请求就按新值算。</summary>
    [Fact]
    public async Task The_window_follows_the_runtime_setting()
    {
        var user = NewUser();

        _resetMinutes = 30;
        (await ResetTokenIsValidAsync(user, TimeSpan.FromMinutes(45))).ShouldBeFalse();

        _resetMinutes = 60;
        (await ResetTokenIsValidAsync(user, TimeSpan.FromMinutes(45))).ShouldBeTrue();
    }

    /// <summary>反向对照：邮箱确认等其它令牌不受重置有效期影响，仍是默认提供者的 1 天。</summary>
    [Fact]
    public async Task Other_tokens_keep_their_own_lifetime()
    {
        _resetMinutes = 30;
        var user = NewUser();

        using var scope = ServiceProvider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var name = manager.Options.Tokens.EmailConfirmationTokenProvider;
        var provider = (DataProtectorTokenProvider<User>)scope.ServiceProvider.GetRequiredService(
            manager.Options.Tokens.ProviderMap[name].ProviderType);
        var token = MintToken(provider, user, UserManager<User>.ConfirmEmailTokenPurpose, DateTimeOffset.UtcNow - TimeSpan.FromHours(2));

        (await manager.VerifyUserTokenAsync(user, name, UserManager<User>.ConfirmEmailTokenPurpose, token)).ShouldBeTrue();
    }

    /// <summary>每次读取都重新构造的选项监视器：用例要在服务已建好之后改设置。</summary>
    private sealed class LiveOptionsMonitor(Func<TnziIdentityOptions> factory) : IOptionsMonitor<TnziIdentityOptions>
    {
        public TnziIdentityOptions CurrentValue => factory();

        public TnziIdentityOptions Get(string? name) => factory();

        public IDisposable? OnChange(Action<TnziIdentityOptions, string?> listener) => null;
    }
}
