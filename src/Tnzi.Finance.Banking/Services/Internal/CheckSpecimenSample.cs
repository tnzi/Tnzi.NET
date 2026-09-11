namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// 版式样张的占位数据（框架自带一份，消费应用不必各编一套）
/// </summary>
/// <remarks>
/// 样张回答的是<b>「这套版式跟我打印机里那叠纸对不对得上」</b>，所以数据本身不重要，
/// 重要的是每个位置都<b>有东西</b>：抬头、支票号、日期、收款人与地址、数字金额、
/// 大写金额、银行区、摘要、签名区、MICR 带、以及两联存根（含附加行）。
/// 少填一处，用户就看不出那一处会落在纸上的哪里。
/// <para>
/// ★ <b>刻意全部固定、不取当前时间</b>：同一套版式每次渲染出字节相同的样张，
/// 于是呈现端可以缓存、测试可以断言。样张是拿来量几何的，日期新不新没有意义。
/// </para>
/// <para>
/// ★★ <b>路由号与账号一律是 0</b>。样张会被下载、被打印、被截图，而白纸票纸模式下
/// 磁码行是<b>真的会被打出来</b>的 —— 水印挡得住人眼，挡不住读票机。
/// 用真实账号的磁码去印一张"样张"，产出的就是一份磁性编码与真票无异的纸。
/// 所以样张永不接触银行档案里的账号密文（见
/// <see cref="CheckBatchComposer.BuildSpecimenRequest"/> 的 <c>useStoredAccountNumber: false</c>）。
/// </para>
/// </remarks>
internal static class CheckSpecimenSample
{
    /// <summary>样张的不可流通标记（刻意不用 "PREVIEW"：那会被读成某笔真实付款的预览）。</summary>
    internal const string Label = "SPECIMEN - NOT NEGOTIABLE";

    /// <summary>占位账号（全 0：磁码带画得出来，但那不是一份可流通的编码）。</summary>
    internal const string AccountNumber = "000000000";

    /// <summary>占位 US 路由号。</summary>
    internal const string RoutingNumber = "000000000";

    /// <summary>占位 CA 机构号。</summary>
    internal const string InstitutionNumber = "000";

    /// <summary>占位 CA 分行号。</summary>
    internal const string TransitNumber = "00000";

    /// <summary>未绑定银行档案时的占位银行名。</summary>
    internal const string BankName = "Sample Bank";

    /// <summary>未绑定银行档案时的占位账户档案名。</summary>
    internal const string AccountName = "Sample account";

    /// <summary>固定的样张日期（不取 UtcNow，见类型注释）。</summary>
    private static readonly DateTime IssueDate = new(2026, 3, 17, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>样张起始支票号（不分配、不占号，纯粹是纸上的一个数字）。</summary>
    private const long FirstCheckNumber = 1001;

    /// <summary>金额刻意取到千位带角分：大写金额够长，能看出行尾 <c>*</c> 填充与截断的实际效果。</summary>
    private const decimal Amount = 1234.56m;

    private static readonly string[] PayeeAddress =
    {
        "250 Sample Street, Suite 400",
        "Toronto ON  M5H 1A1"
    };

    /// <summary>
    /// 造 <paramref name="count"/> 张占位支票。
    /// </summary>
    /// <remarks>
    /// ★ 张数取自版式目录的「每页几张」：每页三张的版式只画一张，用户就看不出它是三联的 ——
    /// 而"这叠纸一页几张"恰恰是选版式时最要紧的那个问题。
    /// </remarks>
    internal static List<CheckRenderItem> BuildItems(int count, string currency)
    {
        var safeCount = count < 1 ? 1 : count;
        var code = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();

        return Enumerable.Range(0, safeCount).Select(i => new CheckRenderItem
        {
            CheckNumber = FirstCheckNumber + i,
            PayeeName = "Sample Payee Ltd.",
            PayeeAddressLines = [.. PayeeAddress],
            Amount = Amount,
            Currency = code,
            AmountInWords = CheckAmountInWords.Convert(Amount, code),
            IssueDate = IssueDate,
            Memo = "Layout specimen",
            PaymentNumber = "PMT-000000",
            Reference = "Sample reference",
            // 存根附加行也要出现：它是消费应用会用到的槽位，样张里空着就看不出它排在哪
            StubLines =
            [
                new CheckStubLine("Sample field", "Sample value"),
                new CheckStubLine("Second field", "Another value")
            ]
        }).ToList();
    }
}
