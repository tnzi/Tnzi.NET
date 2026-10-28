using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Tnzi.Caching;
using Tnzi.Identity.Events;
using Tnzi.Identity.Services;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 验证码按<b>地址</b>收发：同一个地址的不同写法必须是同一个地址，同一枚码只能用一次。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 账号按规范化的邮箱解析（<c>FindByEmailAsync</c>），而码的存储、查询、重发节流与失败计数此前都按请求原样的字符串：
/// <c>Victim@x.com</c> 与 <c>victim@x.com </c> 解析到同一个账号，却各有一个重发桶、各有 5 次猜测。
/// 再加上「先读计数、失败才递增」与「读出实体、整行写回」两处先查后改，验证码找回密码可以被穷举，同一枚码可以被用两次。
/// </para>
/// <para>
/// 跑在真实 SQLite 上：规范形态存进库、条件更新影响几行、计数器是真的内存缓存，mock 一样都证明不了。
/// </para>
/// </remarks>
public class TwoFactorCodeAddressIntegrationTests : RelationalIdentityIntegrationTestBase
{
    /// <summary>
    /// 模拟「另一个并发请求在本请求查到码之后、标记之前把它用掉」：本请求发出第一条改验证码表的 UPDATE 之前，
    /// 先替那个对手把表里的码全部标成已用。xUnit 在同一个测试类内顺序执行，静态实例只被本类的容器引用。
    /// </summary>
    private static readonly CompetingConsumer Competitor = new();

    public TwoFactorCodeAddressIntegrationTests()
        : base(
            configureIdentity: o => o.Otp.ResendIntervalSeconds = 60,
            configureServices: s => s.ConfigureDbContext<TestIdentityDbContext>(o => o.AddInterceptors(Competitor)))
    {
        Competitor.Disarm();
    }

    private TwoFactorService CreateService(ICache? cache = null) => new(
        CreateRepository<TwoFactorCode>(),
        UserManager,
        ServiceProvider,
        EventBusMock.Object,
        ServiceProvider.GetRequiredService<IOptionsSnapshot<IdentityOptions>>(),
        cache);

    private async Task<TwoFactorCode> SingleCodeAsync()
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<TwoFactorCode>().AsNoTracking().SingleAsync();
    }

    [Fact]
    public async Task ACode_IsStoredUnderTheCanonicalAddress_DeliveredToTheTypedOne_AndVerifiesFromAnySpelling()
    {
        var service = CreateService();

        Assert.True((await service.SendCodeByAddressAsync(" Victim@Example.com ", TwoFactorType.Email, VerificationCodePurpose.CodeLogin)).Succeeded);

        var stored = await SingleCodeAsync();
        Assert.Equal("VICTIM@EXAMPLE.COM", stored.Address);
        EventBusMock.Verify(
            x => x.PublishAsync(It.Is<TwoFactorCodeSentEvent>(e => e.Address == "Victim@Example.com"), It.IsAny<CancellationToken>()),
            Times.Once);

        var verified = await service.VerifyCodeByAddressAndMarkUsedAsync("victim@example.com", stored.Code, TwoFactorType.Email, VerificationCodePurpose.CodeLogin);
        Assert.True(verified.Succeeded, verified.Message);
        Assert.True((await SingleCodeAsync()).IsUsed);
    }

    /// <summary>★★★ 换个大小写不是一个新的重发桶：否则每个变体各攒一枚有效码，同时存活的码数随变体数增长。</summary>
    [Fact]
    public async Task TheResendThrottle_AppliesAcrossSpellingsOfOneAddress()
    {
        var service = CreateService(Cache);

        Assert.True((await service.SendCodeByAddressAsync("victim@example.com", TwoFactorType.Email, VerificationCodePurpose.PasswordRecovery)).Succeeded);
        var second = await service.SendCodeByAddressAsync("VICTIM@example.com ", TwoFactorType.Email, VerificationCodePurpose.PasswordRecovery);

        Assert.Equal(429, second.Code);
        Assert.Equal(1, DbContext.Set<TwoFactorCode>().AsNoTracking().Count());
    }

    /// <summary>超过列宽的地址答 400、什么都不存也不发：此前交给插入，PostgreSQL 上 500，SQLite 上原样落库。</summary>
    [Fact]
    public async Task AnAddressBeyondTheColumnWidth_IsAValidationError()
    {
        var service = CreateService();
        var address = new string('a', TwoFactorCode.AddressMaxLength) + "@example.com";

        var result = await service.SendCodeByAddressAsync(address, TwoFactorType.Email, VerificationCodePurpose.ChangeContact);

        Assert.Equal(400, result.Code);
        Assert.Equal(0, DbContext.Set<TwoFactorCode>().AsNoTracking().Count());
        EventBusMock.Verify(x => x.PublishAsync(It.IsAny<TwoFactorCodeSentEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// ★★★ 用变体轮流猜：5 次之后，连正确的码也被挡在外面（429）。此前每个变体各有 5 次，
    /// 第 6 次换个写法带着正确的码就进去了。
    /// </summary>
    [Fact]
    public async Task FailuresUnderSpellingVariants_ShareOneCounter_EvenAgainstTheRightCode()
    {
        var service = CreateService(Cache);
        Assert.True((await service.SendCodeByAddressAsync("victim@example.com", TwoFactorType.Email, VerificationCodePurpose.PasswordRecovery)).Succeeded);
        var code = (await SingleCodeAsync()).Code;
        var wrong = code == "000000" ? "111111" : "000000";

        foreach (var variant in new[] { "Victim@example.com", "vIctim@example.com", "viCtim@example.com", "vicTim@example.com", "victim@example.com " })
        {
            Assert.Equal(400, (await service.VerifyCodeByAddressAndMarkUsedAsync(variant, wrong, TwoFactorType.Email, VerificationCodePurpose.PasswordRecovery)).Code);
        }

        var withTheRightCode = await service.VerifyCodeByAddressAndMarkUsedAsync("VICTIM@example.com", code, TwoFactorType.Email, VerificationCodePurpose.PasswordRecovery);

        Assert.Equal(429, withTheRightCode.Code);
        Assert.False((await SingleCodeAsync()).IsUsed);
    }

    /// <summary>
    /// ★★★ 两个请求拿着同一枚码同时到达：先标记的那个赢，另一个必须失败。
    /// 此前是读出实体、改字段、整行写回（WHERE Id = @id），两个请求都读到「未使用」、都写回成功。
    /// </summary>
    [Fact]
    public async Task ACodeConsumedByAConcurrentRequest_IsNotAcceptedASecondTime()
    {
        var service = CreateService(Cache);
        Assert.True((await service.SendCodeByAddressAsync("victim@example.com", TwoFactorType.Email, VerificationCodePurpose.CodeLogin)).Succeeded);
        var code = (await SingleCodeAsync()).Code;

        Competitor.Arm(DbContext.Model.FindEntityType(typeof(TwoFactorCode))!.GetTableName()!);
        var result = await service.VerifyCodeByAddressAndMarkUsedAsync("victim@example.com", code, TwoFactorType.Email, VerificationCodePurpose.CodeLogin);

        Assert.True(Competitor.Fired, "the simulated competing request never ran; the test proves nothing");
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    /// <summary>
    /// 为什么码要寄到账号上的地址、而不是请求里的那个写法：身份模块的默认规范化（NFC + 不变区域大写）
    /// 会把一个<b>不同的字符串</b>折到既有账号上。长 s（U+017F）大写是 S，于是 <c>ſam@example.com</c>
    /// 解析到 <c>sam@example.com</c> 的账号；不区分全角半角的排序规则（SQL Server 默认）折得更多。
    /// 码若寄往请求里的写法，收件的就未必是账号的主人。
    /// </summary>
    [Fact]
    public async Task TheDefaultNormalizer_FoldsADifferentAddressOntoAnExistingAccount()
    {
        // U+017F LATIN SMALL LETTER LONG S
        var longS = (char)0x017F + "am@example.com";
        var owner = await CreateUserAsync(email: "sam@example.com");

        var resolved = await UserManager.FindByEmailAsync(longS);

        Assert.NotNull(resolved);
        Assert.Equal(owner.Id, resolved!.Id);
        Assert.NotEqual(longS, owner.Email);
    }

    private sealed class CompetingConsumer : DbCommandInterceptor
    {
        private string? _tableName;

        public bool Fired { get; private set; }

        public void Arm(string tableName)
        {
            _tableName = tableName;
            Fired = false;
        }

        public void Disarm()
        {
            _tableName = null;
            Fired = false;
        }

        private async Task CompeteAsync(DbCommand command, CancellationToken cancellationToken)
        {
            var text = command.CommandText.TrimStart();
            if (_tableName == null
                || !text.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                || !text.Contains(_tableName, StringComparison.Ordinal))
            {
                return;
            }

            var table = _tableName;
            _tableName = null;
            await using var competitor = command.Connection!.CreateCommand();
            competitor.Transaction = command.Transaction;
            competitor.CommandText = $"UPDATE \"{table}\" SET \"IsUsed\" = 1";
            await competitor.ExecuteNonQueryAsync(cancellationToken);
            Fired = true;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await CompeteAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await CompeteAsync(command, cancellationToken);
            return result;
        }
    }
}
