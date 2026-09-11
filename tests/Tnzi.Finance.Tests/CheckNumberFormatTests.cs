using Moq;
using Tnzi.Finance.Documents.Metadata;

namespace Tnzi.Finance.Tests;

/// <summary>
/// 支票号的定宽呈现：票面、MICR 串行号、消费应用的登记簿三处必须给出同一串字
/// </summary>
/// <remarks>
/// 缺陷是可读性而非合规性：CPA-006 §4.4.4 把串行号字段定为<b>变长</b>，串行号本身也只是
/// 「highly recommended but not mandatory」。第 2 张支票在票面上印成 <c>2</c>、磁码行也是
/// 一位数，而消费应用的银行登记簿里它挤在一堆定长参考号中间 —— 对账时一列扫不动的数字。
/// <para>
/// ★ 本组分三层，缺一层都证明不了这件事真的成立：
/// ①<b>规则本身</b>算得对；②<b>配置到得了渲染请求</b>（不然改配置没反应）；
/// ③<b>渲染请求到得了纸面与磁码行且两者一致</b>（不然纸上一个号、读票机另一个号）。
/// </para>
/// </remarks>
public class CheckNumberFormatTests
{
    // ── ① 规则本身 ───────────────────────────────────────────

    [Theory]
    [InlineData(2, 5, "00002")]
    [InlineData(1001, 5, "01001")]
    [InlineData(2, 1, "2")]          // 1 位 = 不补零，旧行为的显式表达
    [InlineData(2, 8, "00000002")]
    public void Format_PadsToTheRequestedWidth(long number, int digits, string expected)
        => CheckNumberFormat.Format(number, digits).ShouldBe(expected);

    [Fact]
    public void Format_NeverTruncatesANumberWiderThanTheSetting()
    {
        // ★ 位数是**下限**。截断高位印出来的是另一张支票的号 —— 而它看起来完全正常。
        CheckNumberFormat.Format(1234567, 5).ShouldBe("1234567");
    }

    [Fact]
    public void Format_WithoutASetting_UsesTheFrameworkDefault()
        => CheckNumberFormat.Format(7).Length.ShouldBe(CheckNumberFormat.DefaultDigits);

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(99)]
    public void Normalize_FallsBackToTheDefaultForAnythingOutOfRange(int? digits)
        => CheckNumberFormat.Normalize(digits).ShouldBe(CheckNumberFormat.DefaultDigits);

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Validator_RejectsAnOutOfRangeSetting(int digits)
    {
        // 启动期拒绝而不是静默归一：配错了却照常启动，会让人以为自己配的位数生效了 ——
        // 纸印出来才发现不是。运行期的 Normalize 只服务「消费应用自己构造渲染请求」那条路。
        new FinanceCheckOptionsValidator()
            .Validate(null, new FinanceCheckOptions { CheckNumberDigits = digits })
            .Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validator_AcceptsTheShippedDefault()
    {
        new FinanceCheckOptionsValidator()
            .Validate(null, new FinanceCheckOptions())
            .Succeeded.ShouldBeTrue();
    }

    // ── ② 配置 → 渲染请求 ────────────────────────────────────

    [Theory]
    [InlineData(7)]
    [InlineData(3)]
    public void ConfiguredWidth_ReachesTheRenderRequest(int digits)
    {
        // 不接线的话「不许写死、要能改」这条就只是一个没人读的配置项
        var request = BuildComposer(digits).BuildRenderRequest(Account(), [Item(1001)]);

        request.CheckNumberDigits.ShouldBe(digits);
    }

    // ── ③ 渲染请求 → 纸面 + 磁码行 ───────────────────────────

    [Theory]
    [InlineData(null, "01001")]   // 未指定 → 框架默认 5 位
    [InlineData(7, "0001001")]
    [InlineData(1, "1001")]
    public async Task TheFaceAndTheMicrLine_CarryTheSameSerial(int? digits, string expected)
    {
        var html = await RenderBlankStockAsync(1001, digits);

        // 票面（HTML 里 `No. </span> 01001`），以及磁码行 ⑈序号⑈ 的字形形态 C序号C
        html.ShouldContain($">{expected}<");
        html.ShouldContain($"C{expected}C ");
    }

    [Fact]
    public async Task ANumberWiderThanTheSetting_IsPrintedInFullOnBothTheFaceAndTheMicrLine()
    {
        var html = await RenderBlankStockAsync(1234567, digits: 4);

        html.ShouldContain(">1234567<");
        html.ShouldContain("C1234567C ");
    }

    // ── 夹具 ─────────────────────────────────────────────────

    private static async Task<string> RenderBlankStockAsync(long checkNumber, int? digits)
    {
        var request = new CheckRenderRequest
        {
            Layout = CheckLayout.Voucher,
            StockType = CheckStockType.Blank,
            Scheme = BankNumberScheme.CaEft,
            BankName = "Bank of the North",
            AccountName = "Operating",
            InstitutionNumber = "003",
            TransitNumber = "12345",
            AccountNumberPlain = "000123456",
            CheckNumberDigits = digits,
            TemplateName = CheckTemplates.Cpa006Canada,
            Checks = [Item(checkNumber)]
        };

        return await CheckTemplateHarness.RenderByTemplateNameAsync(CheckTemplates.Cpa006Canada, request);
    }

    private static CheckRenderItem Item(long checkNumber) => new()
    {
        CheckNumber = checkNumber,
        PayeeName = "Northwind Supplies",
        Amount = 1234.56m,
        Currency = "CAD",
        AmountInWords = "One Thousand Two Hundred Thirty-Four and 56/100 Dollars",
        IssueDate = new DateTime(2026, 3, 17, 0, 0, 0, DateTimeKind.Utc)
    };

    private static BankAccount Account() => new()
    {
        Id = Guid.NewGuid(),
        AccountId = Guid.NewGuid(),
        Name = "Operating",
        Scheme = BankNumberScheme.CaEft,
        InstitutionNumber = "003",
        TransitNumber = "12345",
        CheckStockType = CheckStockType.PrePrinted
    };

    private static CheckBatchComposer BuildComposer(int digits)
    {
        var financeOptions = new Mock<IOptionsSnapshot<FinanceOptions>>();
        financeOptions.SetupGet(o => o.Value).Returns(new FinanceOptions());

        var checkOptions = new Mock<IOptionsSnapshot<FinanceCheckOptions>>();
        checkOptions.SetupGet(o => o.Value).Returns(new FinanceCheckOptions { CheckNumberDigits = digits });

        return new CheckBatchComposer(
            Mock.Of<IReadOnlyRepository<PaymentEntry, Guid>>(),
            Mock.Of<IReadOnlyRepository<BankAccount, Guid>>(),
            Mock.Of<IReadOnlyRepository<BankCheck, Guid>>(),
            Mock.Of<IReadOnlyRepository<Vendor, Guid>>(),
            new CheckIssuerResolver(new ConfigurationBuilder().Build(), financeOptions.Object),
            Mock.Of<IFinanceDataProtector>(),
            financeOptions.Object,
            checkOptions.Object);
    }
}
