using Microsoft.Extensions.Options;
using Tnzi.Identity.Options;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 密码到期开着、密码历史关着（<c>PasswordHistoryCount = 0</c>，出厂默认）。
/// </summary>
/// <remarks>
/// ★★★ 两项是设置中心里互相独立的开关，而到期的参照时间「最近一次改密」此前只来自密码历史：
/// 历史关着就一行都不写，参照时间永远回退到账号创建时间。于是账号满 N 天之后每一次登录都要求改密，
/// 改完下一次登录照样到期，永远走不出去。最近一次改密的那一行与复用检查无关，总要留着。
/// </remarks>
public class PasswordExpiryWithoutHistoryIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private readonly PasswordPolicyService _service;

    public PasswordExpiryWithoutHistoryIntegrationTests()
        : base(configureIdentity: options =>
        {
            options.PasswordPolicy.PasswordHistoryCount = 0;
            options.PasswordPolicy.PasswordExpirationDays = 90;
        })
    {
        _service = new PasswordPolicyService(
            CreateRepository<PasswordHistory>(),
            UserManager,
            ServiceProvider,
            ServiceProvider.GetRequiredService<IOptionsMonitor<IdentityOptions>>());
    }

    /// <summary>账号建了 100 天、刚改过密码：不再判到期。</summary>
    [Fact]
    public async Task AfterAChange_AnOldAccountIsNoLongerExpired()
    {
        var user = await CreateUserAsync(creationTime: DateTime.UtcNow.AddDays(-100));
        Assert.True((await _service.CheckPasswordExpirationAsync(user.Id)).IsExpired);

        await _service.SavePasswordHistoryAsync(user.Id, UserManager.PasswordHasher.HashPassword(user, "Changed123!"));

        var afterChange = await _service.CheckPasswordExpirationAsync(user.Id);
        Assert.False(afterChange.IsExpired);
    }

    /// <summary>只留最近的那一行：历史关着时它只是一个时间戳，不是一份可供比对的旧密码清单。</summary>
    [Fact]
    public async Task RepeatedChanges_KeepOnlyTheLatestRow()
    {
        var user = await CreateUserAsync();

        await _service.SavePasswordHistoryAsync(user.Id, UserManager.PasswordHasher.HashPassword(user, "First123!"));
        await _service.SavePasswordHistoryAsync(user.Id, UserManager.PasswordHasher.HashPassword(user, "Second123!"));
        await _service.SavePasswordHistoryAsync(user.Id, UserManager.PasswordHasher.HashPassword(user, "Third123!"));

        Assert.Single(DbContext.PasswordHistories.Where(h => h.UserId == user.Id));
    }

    /// <summary>对照组：留着那一行不等于打开了复用检查，刚用过的密码照样可以再设。</summary>
    [Fact]
    public async Task TheKeptRow_DoesNotTurnOnReuseChecking()
    {
        var user = await CreateUserAsync();
        await _service.SavePasswordHistoryAsync(user.Id, UserManager.PasswordHasher.HashPassword(user, "Reused123!"));

        Assert.False(await _service.CheckPasswordHistoryAsync(user.Id, "Reused123!"));
    }
}
