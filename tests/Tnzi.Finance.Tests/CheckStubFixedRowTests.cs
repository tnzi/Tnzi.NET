using Tnzi.Finance.Documents.Metadata;

namespace Tnzi.Finance.Tests;

/// <summary>
/// 存根的固定行：没有值的那几行不印，一直有值的那几行一直印
/// </summary>
/// <remarks>
/// 缺陷形态：固定行是模板无条件排的，于是一张工资支票的存根读起来是
/// <c>Payment</c> / <c>Reference</c> / <c>Memo</c> 三个标签后面什么都没有 ——
/// 而净额发放本来就没有参考号、通常也没有摘要，所以这是一家公司**实际印出来的大多数存根**的样子。
/// <para>
/// ★ <b>空标签行读起来是「信息缺失」而不是「信息不存在」</b>：拿到存根的人分不出
/// 「摘要本来就是空的」与「摘要没印出来」。它还白占存根高度 —— 而存根的全部约束就是它很小
/// （消费应用给的附加行上限 8 条，正是为此）。
/// </para>
/// <para>
/// ★★ <b>与消费应用附加行的规则刻意相反，不要「统一」它们</b>：
/// <c>CheckStubLineLimits.Normalize</c> 丢掉的是**没有标签**的行，而**有标签没有值**的附加行
/// 它<b>保留</b>（那是一行分节标题，是附加行机制里有意义的用法）。固定行的标签由模板写死，
/// 空值只可能意味着「这笔付款没有这项」。两条规则各自成立，本组两边都断言。
/// </para>
/// </remarks>
public class CheckStubFixedRowTests
{
    /// <summary>
    /// 一张支票总是有的四项（票号 / 日期 / 收款人 / 金额）：空了就说明出了别的问题，那时更应该看得见。
    /// </summary>
    /// <remarks>
    /// ★ 按<b>条数</b>而不是按标签断言：加拿大版式写 "Cheque number"、美式版式写 "Check number"，
    /// 那是各自版式有意的用词。把标签写进测试等于把两种拼写都钉死，而它们本来就该能被改
    /// —— 版式是可编辑的业务资产。这一条要守的是「这四行一直在」，与它们叫什么无关。
    /// </remarks>
    private const int AlwaysPrintedRows = 4;

    /// <summary>只有这笔付款带了才印的三项（五套版式的标签一致，故可按标签断言）。</summary>
    private static readonly string[] OnlyWhenPresent = ["Payment", "Reference", "Memo"];

    /// <summary>两联存根，每一行在产物里出现两次。</summary>
    private const int VoucherCopies = 2;

    /// <summary>
    /// 带存根联的出厂版式。<b>从模板正文推导而不是手写清单</b> —— 新增一套带存根的版式
    /// 会自动进入本组，而手写清单最典型的失效方式就是「加了版式忘了加进清单」。
    /// </summary>
    public static TheoryData<string> StubBearingLayouts()
    {
        var data = new TheoryData<string>();
        foreach (var builtIn in BuiltInCheckTemplates.All)
        {
            if (CheckTemplateHarness.ReadEmbeddedTemplate(builtIn.ResourceFile).Contains("stub-table", StringComparison.Ordinal))
                data.Add(builtIn.ResourceFile);
        }

        // 反空转：一条都没推导出来与「全都合规」在输出上不可区分
        data.Count.ShouldBeGreaterThan(1, "no stub-bearing layout was discovered; this whole class would be vacuous.");
        return data;
    }

    [Theory]
    [MemberData(nameof(StubBearingLayouts))]
    public async Task AFullyPopulatedStub_StillPrintsEveryRow(string resourceFile)
    {
        // 「字段都填满时排出来的纸不变」—— 本轮改的是空值那一支，填满的那一支一行都不该少
        var stub = await RenderStubAsync(resourceFile, paymentNumber: "PMT-000041", reference: "Invoice 80", memo: "August retainer");

        HeaderCells(stub).ShouldBe((AlwaysPrintedRows + OnlyWhenPresent.Length) * VoucherCopies,
            $"{resourceFile}: a fully populated stub must still carry all seven fixed rows on both copies.");
        foreach (var label in OnlyWhenPresent)
            Rows(stub, label).ShouldBe(VoucherCopies, $"{resourceFile}: '{label}' should appear once per voucher copy.");

        // 条件包装没有打乱顺序
        var order = OnlyWhenPresent.Select(l => stub.IndexOf($"<th>{l}</th>", StringComparison.Ordinal)).ToList();
        order.ShouldBe(order.OrderBy(i => i).ToList(), $"{resourceFile}: the optional rows changed order.");
    }

    [Theory]
    [MemberData(nameof(StubBearingLayouts))]
    public async Task RowsTheChequeAlwaysCarries_ArePrintedEvenSoItIsObviousWhenOneIsBlank(string resourceFile)
    {
        var stub = await RenderStubAsync(resourceFile, paymentNumber: null, reference: null, memo: null);

        HeaderCells(stub).ShouldBe(AlwaysPrintedRows * VoucherCopies,
            $"{resourceFile}: the cheque number, date, payee and amount rows must survive an otherwise empty payment.");
        // 值也真的在那里（只数标签的话，四个标签配四个空格子同样满足上一条）
        Occurrences(stub, "Sarah Williams").ShouldBe(VoucherCopies, $"{resourceFile}: the payee is missing from the stub.");
        Occurrences(stub, "3,502.55").ShouldBe(VoucherCopies, $"{resourceFile}: the amount is missing from the stub.");
    }

    /// <summary>
    /// ★★ 「一直印」的那几行<b>连值为空时也要印</b>：那正是它们与可选行的全部差别。
    /// </summary>
    /// <remarks>
    /// 收款人是这四行里唯一可能为空的（票号 / 日期 / 金额由模型工厂格式化，恒有值），
    /// 所以它是唯一测得出这条差别的一行 —— 也正是最可能被人「顺手统一一下」包进条件里的那一行。
    /// 一张收款人为空的支票是出了别的问题，那时更需要它在纸上看得见，而不是安静消失。
    /// <para>
    /// 没有这一条，把 <c>Payee</c> 也包进 <c>IsNullOrWhiteSpace</c> 的改动**一条测试都不会红**
    /// —— 本组第一版就是这样，是变异验证抓出来的。
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(StubBearingLayouts))]
    public async Task AnAlwaysPrintedRow_SurvivesEvenWhenItsOwnValueIsBlank(string resourceFile)
    {
        var stub = await RenderStubAsync(resourceFile, paymentNumber: null, reference: null, memo: null, payeeName: "  ");

        HeaderCells(stub).ShouldBe(AlwaysPrintedRows * VoucherCopies,
            $"{resourceFile}: a blank payee must still take its row - a cheque always has a payee, so an empty "
            + "one means something went wrong upstream and must stay visible. Only Payment / Reference / Memo "
            + "disappear when empty.");
    }

    [Theory]
    [MemberData(nameof(StubBearingLayouts))]
    public async Task ARowWithNoValue_IsNotPrintedAtAll(string resourceFile)
    {
        // 工资支票的常态：净额发放没有参考号，通常也没有摘要
        var stub = await RenderStubAsync(resourceFile, paymentNumber: null, reference: null, memo: null);

        foreach (var label in OnlyWhenPresent)
        {
            Rows(stub, label).ShouldBe(0,
                $"{resourceFile}: '{label}' is printed with nothing after it. A labelled blank row reads as "
                + "missing information rather than absent information, and spends stub height on nothing.");
        }
    }

    [Theory]
    [MemberData(nameof(StubBearingLayouts))]
    public async Task AWhitespaceOnlyValue_CountsAsNoValue(string resourceFile)
    {
        // 空串与「一个空格」在纸上完全一样；判据必须是 IsNullOrWhiteSpace 而不是 IsNullOrEmpty
        var stub = await RenderStubAsync(resourceFile, paymentNumber: "   ", reference: "\t", memo: " ");

        foreach (var label in OnlyWhenPresent)
            Rows(stub, label).ShouldBe(0, $"{resourceFile}: '{label}' survived with a whitespace-only value.");
    }

    [Theory]
    [MemberData(nameof(StubBearingLayouts))]
    public async Task TheDecisionIsPerRow_NotAllOrNothing(string resourceFile)
    {
        var stub = await RenderStubAsync(resourceFile, paymentNumber: null, reference: null, memo: "August retainer");

        Rows(stub, "Memo").ShouldBe(2, $"{resourceFile}: a memo that IS present must still print.");
        Rows(stub, "Payment").ShouldBe(0, $"{resourceFile}: an absent payment number must not print.");
        Rows(stub, "Reference").ShouldBe(0, $"{resourceFile}: an absent reference must not print.");
    }

    /// <summary>
    /// ★★ 消费应用附加行走的是<b>相反</b>的规则：有标签没有值的行是分节标题，必须留着。
    /// </summary>
    /// <remarks>
    /// 没有这一条，日后有人为了「一致」把附加行也按空值丢掉，而那会静默删掉工资单上的分节标题。
    /// </remarks>
    [Theory]
    [MemberData(nameof(StubBearingLayouts))]
    public async Task ASuppliedLineWithNoValue_IsStillPrinted(string resourceFile)
    {
        var stub = await RenderStubAsync(resourceFile, paymentNumber: null, reference: null, memo: null,
            stubLines: [new CheckStubLine("Earnings", null), new CheckStubLine("Gross pay", "5,265.00")]);

        stub.Contains("<th>Earnings</th>", StringComparison.Ordinal).ShouldBeTrue(
            $"{resourceFile}: a supplied line with a label and no value is a SECTION HEADING and must survive. "
            + "The fixed rows follow the opposite rule on purpose - see CheckStubLineLimits.Normalize.");
        stub.Contains("<th>Gross pay</th>", StringComparison.Ordinal).ShouldBeTrue($"{resourceFile}: supplied line lost.");
    }

    // ── 夹具 ─────────────────────────────────────────────────

    private static int Rows(string stub, string label) => Occurrences(stub, $"<th>{label}</th>");

    /// <summary>存根表里的表头格子总数（两联合计）。</summary>
    private static int HeaderCells(string stub) => Occurrences(stub, "<th>");

    private static int Occurrences(string haystack, string needle) => haystack.Split(needle).Length - 1;

    /// <summary>
    /// 渲染后只取<b>存根部分</b>的标记（从第一张存根表起）。
    /// </summary>
    /// <remarks>
    /// ★ 必须切片：票面上也有 "Memo" 字样（<c>.memo-label</c>），整篇计数会把它数进来，
    /// 而票面那条摘要线是另一回事 —— 支票正面留一条空摘要线是常规做法，本轮不碰它。
    /// 切片失败即红：切不到与「一切正常」在输出上不可区分。
    /// </remarks>
    private static async Task<string> RenderStubAsync(
        string resourceFile, string? paymentNumber, string? reference, string? memo,
        List<CheckStubLine>? stubLines = null, string? payeeName = "Sarah Williams")
    {
        var html = await RenderAsync(resourceFile, paymentNumber, reference, memo, stubLines, payeeName);
        var start = html.IndexOf("class=\"stub-table\"", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(-1, $"{resourceFile}: no stub table in the rendered document.");
        return html[start..];
    }

    private static Task<string> RenderAsync(
        string resourceFile, string? paymentNumber, string? reference, string? memo,
        List<CheckStubLine>? stubLines = null, string? payeeName = "Sarah Williams")
        => CheckTemplateHarness.RenderAsync(resourceFile, new CheckRenderRequest
        {
            Layout = CheckLayout.Voucher,
            StockType = CheckStockType.PrePrinted,
            Scheme = BankNumberScheme.CaEft,
            BankName = "Bank of the North",
            AccountName = "Operating",
            InstitutionNumber = "003",
            TransitNumber = "12345",
            TemplateName = BuiltInCheckTemplates.All.Single(t => t.ResourceFile == resourceFile).Name,
            Checks =
            [
                new CheckRenderItem
                {
                    CheckNumber = 3,
                    PayeeName = payeeName,
                    Amount = 3502.55m,
                    Currency = "CAD",
                    AmountInWords = "Three Thousand Five Hundred Two and 55/100 Dollars",
                    IssueDate = new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc),
                    PaymentNumber = paymentNumber,
                    Reference = reference,
                    Memo = memo,
                    StubLines = stubLines ?? []
                }
            ]
        });
}
