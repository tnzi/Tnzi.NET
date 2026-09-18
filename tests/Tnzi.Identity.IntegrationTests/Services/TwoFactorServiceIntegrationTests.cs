using Microsoft.Extensions.Options;
using Tnzi.Identity.Events;
using Tnzi.Identity.Options;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

public class TwoFactorServiceIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private readonly TwoFactorService _service;

    public TwoFactorServiceIntegrationTests()
    {
        _service = new TwoFactorService(
            CreateRepository<TwoFactorCode>(),
            UserManager,
            ServiceProvider,
            EventBusMock.Object,
            ServiceProvider.GetRequiredService<IOptionsSnapshot<IdentityOptions>>(),
            cache: null);
    }

    [Fact]
    public async Task SendSmsCodeAsync_WithValidInput_ReturnsTrue()
    {
        var user = await CreateUserAsync(phoneNumber: "13800138000");

        var result = await _service.SendSmsCodeAsync(user.Id, user.PhoneNumber!, VerificationCodePurpose.TwoFactor);

        Assert.True(result.Succeeded);
        Assert.Single(DbContext.Set<TwoFactorCode>());
        Assert.Equal(TwoFactorType.Sms, DbContext.Set<TwoFactorCode>().Single().Type);
        EventBusMock.Verify(x => x.PublishAsync(It.IsAny<TwoFactorCodeSentEvent>(), default), Times.Once);
    }

    [Fact]
    public async Task SendEmailCodeAsync_WithValidInput_ReturnsTrue()
    {
        var user = await CreateUserAsync(email: "2fa@example.com");

        var result = await _service.SendEmailCodeAsync(user.Id, user.Email!, VerificationCodePurpose.TwoFactor);

        Assert.True(result.Succeeded);
        Assert.Equal(TwoFactorType.Email, DbContext.Set<TwoFactorCode>().Single().Type);
    }

    [Fact]
    public async Task VerifyCodeAsync_WithValidCode_ReturnsTrue()
    {
        var user = await CreateUserAsync(email: "verify@example.com");
        DbContext.Set<TwoFactorCode>().Add(new TwoFactorCode
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Address = user.Email!,
            Code = "123456",
            Type = TwoFactorType.Email,
            Purpose = VerificationCodePurpose.TwoFactor,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            IsUsed = false,
            CreationTime = DateTime.UtcNow
        });
        await SaveChangesAsync();

        var result = await _service.VerifyCodeAsync(user.Id, "123456", TwoFactorType.Email, VerificationCodePurpose.TwoFactor);

        Assert.True(result.Succeeded);
        Assert.True(DbContext.Set<TwoFactorCode>().Single().IsUsed);
    }

    [Fact]
    public async Task VerifyCodeAsync_WithExpiredCode_ReturnsFalse()
    {
        var user = await CreateUserAsync(email: "expired@example.com");
        DbContext.Set<TwoFactorCode>().Add(new TwoFactorCode
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Address = user.Email!,
            Code = "654321",
            Type = TwoFactorType.Email,
            Purpose = VerificationCodePurpose.TwoFactor,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            IsUsed = false,
            CreationTime = DateTime.UtcNow.AddMinutes(-2)
        });
        await SaveChangesAsync();

        var result = await _service.VerifyCodeAsync(user.Id, "654321", TwoFactorType.Email, VerificationCodePurpose.TwoFactor);

        Assert.False(result.Succeeded);
        Assert.False(DbContext.Set<TwoFactorCode>().Single().IsUsed);
    }

    /// <summary>
    /// ★★★ 用途绑定：为「验证码登录」发出的码，拿去完成 2FA 挑战必须无效。
    /// </summary>
    /// <remarks>
    /// 这条<b>只能</b>跑在真实仓储上：mock 能证明用途参数传对了，证明不了查询谓词真的按它过滤 ——
    /// 而后者才是「一枚登录码能不能确认一笔转账」的分界。
    /// <para>
    /// 用途不符与码不对返回同一种结果（400 无效码），不单独区分：区分等于告诉试探者
    /// 「这枚码是真的，只是用错了地方」。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task VerifyCode_WithCodeIssuedForAnotherPurpose_IsRejected()
    {
        var user = await CreateUserAsync(email: "purpose@example.com");
        var result = await _service.SendCodeToUserAsync(user.Id, TwoFactorType.Email, VerificationCodePurpose.CodeLogin);
        Assert.True(result.Succeeded);

        var issued = DbContext.Set<TwoFactorCode>().Single();
        Assert.Equal(VerificationCodePurpose.CodeLogin, issued.Purpose);

        // 同一地址、同一渠道、同一枚码 —— 只有用途不同。
        var crossUse = await _service.VerifyCodeByAddressAndMarkUsedAsync(
            user.Email!, issued.Code, TwoFactorType.Email, VerificationCodePurpose.TwoFactor);

        Assert.False(crossUse.Succeeded);
        Assert.False(DbContext.Set<TwoFactorCode>().Single().IsUsed);

        // 对照组：用本来的用途验，同一枚码照常通过 —— 否则上面那条在「什么都验不过」的实现上也是绿的。
        var properUse = await _service.VerifyCodeByAddressAndMarkUsedAsync(
            user.Email!, issued.Code, TwoFactorType.Email, VerificationCodePurpose.CodeLogin);

        Assert.True(properUse.Succeeded);
        Assert.True(DbContext.Set<TwoFactorCode>().Single().IsUsed);
    }

    /// <summary>
    /// 迁移之前写入的历史行（<c>Purpose = Unknown</c>）永不匹配任何验证请求。
    /// </summary>
    /// <remarks>
    /// ★ 这是刻意的取舍：留一个「万能用途」值可以让在途的码不失效，
    /// 但它同时是一个能绕过全部用途绑定的后门。宁可让那几分钟内的码重发一次。
    /// </remarks>
    [Fact]
    public async Task VerifyCode_WithLegacyUnknownPurposeRow_IsRejected()
    {
        var user = await CreateUserAsync(email: "legacy@example.com");
        DbContext.Set<TwoFactorCode>().Add(new TwoFactorCode
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Address = user.Email!,
            Code = "111222",
            Type = TwoFactorType.Email,
            Purpose = VerificationCodePurpose.Unknown,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            IsUsed = false,
            CreationTime = DateTime.UtcNow
        });
        await SaveChangesAsync();

        var result = await _service.VerifyCodeByAddressAndMarkUsedAsync(
            user.Email!, "111222", TwoFactorType.Email, VerificationCodePurpose.CodeLogin);

        Assert.False(result.Succeeded);
        Assert.False(DbContext.Set<TwoFactorCode>().Single().IsUsed);
    }

    /// <summary>
    /// 发码时拒绝 <c>Unknown</c>：那等于发一枚立刻就作废的码，
    /// 失败要发生在发送之前，而不是等用户输了码才发现。
    /// </summary>
    [Fact]
    public async Task SendCode_WithUnknownPurpose_IsRejectedBeforeSending()
    {
        var user = await CreateUserAsync(email: "nopurpose@example.com");

        var result = await _service.SendCodeByAddressAsync(
            user.Email!, TwoFactorType.Email, VerificationCodePurpose.Unknown, user.Id);

        Assert.False(result.Succeeded);
        Assert.Empty(DbContext.Set<TwoFactorCode>());
        EventBusMock.Verify(x => x.PublishAsync(It.IsAny<TwoFactorCodeSentEvent>(), default), Times.Never);
    }

    /// <summary>
    /// <c>SendCodeToUserAsync</c> 要求地址<b>已验证</b>，而不是「填了就行」。
    /// </summary>
    /// <remarks>
    /// 往一个未经证实的地址发码，等于让任何能改这个字段的人把码引到自己那里去。
    /// </remarks>
    [Fact]
    public async Task SendCodeToUser_WithUnconfirmedAddress_IsRejected()
    {
        var user = await CreateUserAsync(email: "unconfirmed@example.com");
        user.EmailConfirmed = false;
        await UserManager.UpdateAsync(user);

        var result = await _service.SendCodeToUserAsync(user.Id, TwoFactorType.Email, VerificationCodePurpose.StepUp);

        Assert.False(result.Succeeded);
        Assert.Empty(DbContext.Set<TwoFactorCode>());
    }

    [Fact]
    public async Task DisableTwoFactorAsync_WithValidUserId_Disables2FA()
    {
        var user = await CreateUserAsync(email: "disable@example.com");
        await UserManager.SetTwoFactorEnabledAsync(user, true);
        DbContext.Set<TwoFactorCode>().Add(new TwoFactorCode
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Address = user.Email!,
            Code = "123123",
            Type = TwoFactorType.Email,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            IsUsed = false,
            CreationTime = DateTime.UtcNow
        });
        await SaveChangesAsync();

        var result = await _service.DisableTwoFactorAsync(user.Id);

        Assert.True(result.Succeeded);
        Assert.False((await UserManager.FindByIdAsync(user.Id.ToString()))!.TwoFactorEnabled);
        Assert.Empty(DbContext.Set<TwoFactorCode>());
    }

    /// <summary>
    /// ★ 验证码表此前没有任何清理路径：明文码 + 收件地址 + 用途无限期留存（已用的置 IsUsed 后永久保留），
    /// 为清理而建的 ExpiresAt 索引零消费者。清扫只看 <c>ExpiresAt</c> 过了保留期多久，用没用过都删 ——
    /// 验码窗口只有 ExpirationMinutes，过期之后这一行对任何流程都没有意义。
    /// </summary>
    [Fact]
    public async Task CleanExpiredCodes_RemovesRowsPastRetention_KeepsLiveOnes()
    {
        var now = DateTime.UtcNow;
        var user = await CreateUserAsync(email: "retention@example.com");
        TwoFactorCode Row(DateTime expiresAt, bool used) => new()
        {
            Id = Guid.NewGuid(), UserId = user.Id, Address = user.Email!, Code = "111111",
            Type = TwoFactorType.Email, Purpose = VerificationCodePurpose.TwoFactor,
            ExpiresAt = expiresAt, IsUsed = used, CreationTime = expiresAt.AddMinutes(-10)
        };
        var stale = Row(now.AddHours(-30), used: true);
        var staleUnused = Row(now.AddHours(-25), used: false);
        var expiredButRecent = Row(now.AddHours(-1), used: false);
        var live = Row(now.AddMinutes(5), used: false);
        DbContext.Set<TwoFactorCode>().AddRange(stale, staleUnused, expiredButRecent, live);
        await SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        // 默认保留 24 小时（RetentionHours 的默认值）。
        var removed = await _service.CleanExpiredCodesAsync();

        Assert.Equal(2, removed);
        var remaining = DbContext.Set<TwoFactorCode>().Select(c => c.Id).ToHashSet();
        Assert.Equal(new HashSet<Guid> { expiredButRecent.Id, live.Id }, remaining);
    }
}
