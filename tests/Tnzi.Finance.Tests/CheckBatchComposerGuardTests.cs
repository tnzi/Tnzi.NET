using Moq;

namespace Tnzi.Finance.Tests;

/// <summary>
/// 空白票纸打印前置守卫：它必须验证账号<b>解得开</b>，而不只是密文在不在。
/// </summary>
/// <remarks>
/// <para>
/// 守卫此前只校验 <c>AccountNumberEncrypted</c> 非空 + <c>IsConfigured</c>。密钥轮换后
/// （或把库恢复到另一把密钥的环境后）这两个条件<b>都仍然成立</b>，而解密会失败。
/// </para>
/// <para>
/// 之所以严重，在于失败被吞掉的位置：<c>BuildRenderRequest</c> 把解密异常记成一条
/// <c>LogWarning</c> 并让 <c>AccountNumberPlain</c> 留 null，渲染器于是丢掉整条 MICR 行 ——
/// 渲染本身<b>成功</b>，所以 <c>CheckService.PrintAsync</c> 的工作单元照常提交。而它是
/// <b>先</b>分配支票号、写 Issued 登记、把号回写进付款单参考号，<b>然后</b>才渲染的。
/// 于是一批 20 张的结果是：20 个号被消耗、20 条登记落库、20 张纸上没有 MICR，
/// 银行全数拒收。守卫检查「有没有」而不是「解不解得开」，恰好放行了它唯一要拦的场景。
/// </para>
/// </remarks>
public class CheckBatchComposerGuardTests
{
    [Fact]
    public void Blank_stock_is_blocked_when_the_stored_account_number_cannot_be_decrypted()
    {
        var composer = CreateComposer(protector => protector
            .Setup(p => p.Unprotect(It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new InvalidOperationException("key mismatch")));

        var result = composer.ValidateBlankStockPrintable(BlankStockAccount());

        result.Succeeded.ShouldBeFalse(
            "密钥轮换后密文还在、加密仍配置着，只有真解一次才发现解不开 —— "
            + "放行的代价是一批支票号被消耗在不可流通的纸上");
        result.Code.ShouldBe(400);
    }

    [Fact]
    public void Blank_stock_passes_when_the_stored_account_number_decrypts()
    {
        var composer = CreateComposer(protector => protector
            .Setup(p => p.Unprotect(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("123456789012"));

        composer.ValidateBlankStockPrintable(BlankStockAccount()).Succeeded.ShouldBeTrue();
    }

    /// <summary>预印票纸的 MICR 已印在纸上，不需要账号明文，故整条守卫跳过。</summary>
    [Fact]
    public void Preprinted_stock_never_touches_the_protector()
    {
        var protectorMock = new Mock<IFinanceDataProtector>(MockBehavior.Strict);
        var composer = CreateComposer(protectorMock);

        var account = BlankStockAccount();
        account.CheckStockType = CheckStockType.PrePrinted;

        composer.ValidateBlankStockPrintable(account).Succeeded.ShouldBeTrue();
        protectorMock.VerifyNoOtherCalls();
    }

    private static BankAccount BlankStockAccount() => new()
    {
        Id = Guid.NewGuid(),
        AccountId = Guid.NewGuid(),
        Name = "Operating",
        Scheme = BankNumberScheme.UsAba,
        RoutingNumber = "021000021",
        AccountNumberEncrypted = "ciphertext-that-no-longer-decrypts",
        CheckStockType = CheckStockType.Blank,
    };

    private static CheckBatchComposer CreateComposer(Action<Mock<IFinanceDataProtector>> setup)
    {
        var protector = new Mock<IFinanceDataProtector>();
        protector.SetupGet(p => p.IsConfigured).Returns(true);
        setup(protector);
        return CreateComposer(protector);
    }

    private static CheckBatchComposer CreateComposer(Mock<IFinanceDataProtector> protector)
    {
        if (protector.Behavior != MockBehavior.Strict)
            protector.SetupGet(p => p.IsConfigured).Returns(true);

        var options = Microsoft.Extensions.Options.Options.Create(new FinanceOptions());
        var snapshot = new Mock<IOptionsSnapshot<FinanceOptions>>();
        snapshot.SetupGet(o => o.Value).Returns(options.Value);

        var configuration = new ConfigurationBuilder().Build();

        return new CheckBatchComposer(
            Mock.Of<IReadOnlyRepository<PaymentEntry, Guid>>(),
            Mock.Of<IReadOnlyRepository<BankAccount, Guid>>(),
            Mock.Of<IReadOnlyRepository<BankCheck, Guid>>(),
            Mock.Of<IReadOnlyRepository<Vendor, Guid>>(),
            new CheckIssuerResolver(configuration, snapshot.Object),
            protector.Object,
            snapshot.Object);
    }
}
