using System.Text.RegularExpressions;
using Tnzi.Finance.Documents.Metadata;

namespace Tnzi.Finance.Tests;

/// <summary>
/// CPA Standard 006 §5.4.3：数字金额框与美元符号<b>四周</b>的 0.64cm 净空门禁
/// </summary>
/// <remarks>
/// 这道门禁存在的原因是一次真实事故：<c>check-cpa006-ca-window</c> 把 "DOLLARS" 放在
/// 大写金额行末尾（其余五套版式的做法），而<b>那一行的右端正是数字金额框</b> ——
/// 字压过美元符号 9mm、压进框内 4mm。**屏幕预览看不出来**（预览与打印是同一份 HTML），
/// 印出来就是一张银行可以拒收的可流通票据。
/// <para>
/// ★★ <b>它的第一版只看得见「左右」，看不见「上下」</b>：判据是「大写金额行是否与金额框同处一行」，
/// 不同行就直接 return。而 §5.4.3 要的是<b>四周</b>的净空 —— 于是**六套版式无一例外**都在
/// 金额框正下方 6mm 处压着大写金额行（差 0.4mm），窗口式版式另有日期块压在框上方、
/// 银行标识压在框下方，全部一路全绿。**门禁扫不到的那一面与「没有违规」在输出上不可区分**，
/// 这是本仓反复兑现过的教训，而这一次是门禁自己犯的。
/// </para>
/// <para>
/// ★ 门禁<b>从模板几何算</b>而不是看渲染件：净空是 CSS 里的毫米数减出来的，不该只靠人眼。
/// 判据刻意做成<b>结构性</b>的而不是去模拟字体度量 ——「DOLLARS 有多宽」取决于字体与渲染器，
/// 测试里算出来的宽度只会制造虚假的精确感。<b>纵向的代价是模板必须声明高度</b>：
/// 一个由内容撑开的块没有任何人算得出它的下边缘，于是「它离金额框多远」这个问题在样式表里
/// 根本无法回答。故本门禁<b>要求</b>压在金额框上方的元素声明 <c>height</c>，
/// 而不是遇到算不出来就跳过 —— 跳过就是把扫描面又交出去一块。
/// </para>
/// <para>
/// ★ 解析失败必须红，不能安静跳过：门禁的扫描面塌掉与「没有违规」在输出上不可区分。
/// 故每份模板都断言解析出了预期的元素。
/// </para>
/// </remarks>
public class CheckCourtesyAmountClearanceTests
{
    /// <summary>CPA-006 §5.4.3 要求的净空：0.64 cm（0.25 in）。</summary>
    private const decimal RequiredClearanceMm = 6.4m;

    /// <summary>Letter 页宽，用于把 <c>right:</c> 折算成绝对左坐标。</summary>
    private const decimal PageWidthMm = 215.9m;

    /// <summary>
    /// 票面上会与金额区争地方的元素。
    /// </summary>
    /// <remarks>
    /// ★ <b>刻意是一份清单而不是「扫描全部规则」</b>：样式表里还有 <c>.courtesy-box</c> /
    /// <c>.courtesy-value</c> / <c>.courtesy-sign</c>（它们<b>就是</b>金额区本身）、
    /// <c>.preview-watermark</c>（只在预览上出现、半透明、真票没有）、以及页/存根这类结构块。
    /// 把它们一并算进来只会让门禁对着自己报红。清单漏了元素怎么办 —— 见
    /// <see cref="EveryPositionedFaceElement_IsEitherCheckedOrDeliberatelyExempt"/>，
    /// 那一条盯着这份清单本身。
    /// </remarks>
    private static readonly string[] FaceElements =
    [
        "cheque-no", "date-block", "issuer", "pay-label", "payee", "payee-address", "payee-window",
        "legal", "legal-currency", "bank", "memo-label", "memo", "signature",
        // check-3up 把付款参考号印在票面上（那种票纸没有存根联可放）
        "reference"
    ];

    /// <summary>不参与净空判定的元素，以及为什么。</summary>
    private static readonly Dictionary<string, string> Exempt = new()
    {
        ["courtesy"] = "the amount field itself",
        ["courtesy-box"] = "the amount field itself",
        ["courtesy-sign"] = "the amount field itself",
        ["courtesy-value"] = "the amount field itself",
        ["preview-watermark"] = "preview only, semi-transparent, never on a negotiable cheque",
        ["cheque"] = "the cheque body container, not an element printed on it",
        ["cheque-page"] = "the page container",
        ["abs"] = "positioning helper, no geometry of its own",
        ["stub"] = "voucher stub band, below the cheque body",
        ["stub-1"] = "voucher stub band, below the cheque body",
        ["stub-2"] = "voucher stub band, below the cheque body",
        ["slot-1"] = "cheque band offset (3-up)",
        ["slot-2"] = "cheque band offset (3-up)",
        ["slot-3"] = "cheque band offset (3-up)",
        ["perf-1"] = "perforation guide on a band boundary (3-up)",
        ["perf-2"] = "perforation guide on a band boundary (3-up)",
        ["feed-margin"] = "printer feed margin at the foot of the sheet (3-up)",
        ["micr-band"] = "the MICR clear band at the foot of the cheque",
        ["micr-line"] = "inside the MICR band",
        ["micr-readable"] = "inside the MICR band"
    };

    public static TheoryData<string> AllLayouts() =>
    [
        "check-cpa006-ca.cshtml", "check-cpa006-ca-window.cshtml", "check-voucher-top-us.cshtml",
        "check-voucher-middle-us.cshtml", "check-voucher-bottom-us.cshtml", "check-3up.cshtml"
    ];

    /// <summary>
    /// ★★ 本轮新增的那一半：金额区<b>上下左右</b>都要留够净空。
    /// </summary>
    [Theory]
    [MemberData(nameof(AllLayouts))]
    public void AmountField_KeepsTheRequiredClearAreaOnEverySide(string resourceFile)
    {
        var css = ReadEmbeddedTemplate(resourceFile);
        var amount = AmountField(css, resourceFile);
        var checkedElements = 0;

        foreach (var name in FaceElements)
        {
            var rule = RuleMatch(css, name);
            if (!rule.Success)
                continue;   // 并非每套版式都有每个元素（如窗口式没有 .payee）

            var box = Box(rule.Groups[1].Value);
            if (box == null)
                continue;   // 没有 top 的元素不是绝对定位的票面块（如 .payee-name 是窗口块的子元素）

            checkedElements++;
            AssertClear(box.Value, amount, name, resourceFile);
        }

        // 反空转：解析面塌掉（正则改了、类名重命名了）与「全都合规」在输出上不可区分
        checkedElements.ShouldBeGreaterThan(5,
            $"{resourceFile}: only {checkedElements} positioned face elements were found. This gate measures "
            + "nothing if it cannot parse the layout, and reports the same green either way.");
    }

    /// <summary>
    /// 事故本身：金额框所在的那一行右端不得再有独立的币种字样元素。
    /// </summary>
    /// <remarks>
    /// 它没有宽度、由文本撑开，净空算不出来也挡不住 —— 唯一安全的做法是把币种词并进机打的大写金额
    /// （CPA-006 §5.4.1 第 9 条给的另一条路）。
    /// <para>
    /// 判的是<b>样式规则与票面元素</b>，不是「文中出现过这个名字」：模板注释里正需要点名
    /// 这个类去说明为什么不能加回来，按整篇 <c>ShouldNotContain</c> 会被自己的文档绊倒。
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllLayouts))]
    public void WhenTheWrittenAmountSharesTheAmountRow_NoSeparateCurrencyElementMayExist(string resourceFile)
    {
        var css = ReadEmbeddedTemplate(resourceFile);
        var legal = Rule(css, resourceFile, "legal");
        var courtesy = Rule(css, resourceFile, "courtesy");

        if (!SharesRow(legal, courtesy))
            return;   // 大写金额行在自己那一行（其余五套版式）——本条不适用

        HasRule(css, "legal-currency").ShouldBeFalse(
            $"{resourceFile}: the courtesy box shares the written-amount row, so a separate currency element "
            + "cannot fit without breaking the required clear space. "
            + "Use Model.AmountInWordsWithCurrencyText (CPA-006 5.4.1 item 9) instead.");
        Regex.IsMatch(css, @"class=""[^""]*\blegal-currency\b").ShouldBeFalse(
            $"{resourceFile}: a currency element is rendered on the row shared with the courtesy amount.");
    }

    /// <summary>大写金额行独占一行的版式：币种字样元素必须排在该行文本之后，不得与之重叠。</summary>
    [Theory]
    [InlineData("check-cpa006-ca.cshtml")]
    [InlineData("check-voucher-top-us.cshtml")]
    [InlineData("check-voucher-middle-us.cshtml")]
    [InlineData("check-voucher-bottom-us.cshtml")]
    [InlineData("check-3up.cshtml")]
    public void StandaloneCurrencyLabel_StartsAfterTheWrittenAmountLineEnds(string resourceFile)
    {
        var css = ReadEmbeddedTemplate(resourceFile);

        var legal = Rule(css, resourceFile, "legal");
        var currency = Rule(css, resourceFile, "legal-currency");
        var courtesy = Rule(css, resourceFile, "courtesy");

        // 这五套的前提就是"不同行"；哪天有人把它们挪到一起，上面那条 Theory 会接手
        SharesRow(legal, courtesy).ShouldBeFalse(
            $"{resourceFile}: this layout is asserted here on the premise that the written amount and the "
            + "courtesy box sit on different rows.");

        var legalRight = Mm(legal, "left") + Mm(legal, "width");
        Mm(currency, "left").ShouldBeGreaterThanOrEqualTo(legalRight,
            $"{resourceFile}: the currency word starts before the written-amount line ends.");
    }

    /// <summary>
    /// ★ 盯着 <see cref="FaceElements"/> 这份清单本身：样式表里每一条带 <c>top</c> 的绝对定位规则，
    /// 要么被检查、要么在 <see cref="Exempt"/> 里带着理由。
    /// </summary>
    /// <remarks>
    /// 没有这一条，往模板里加一个新元素就等于给它发一张免检通行证 —— 而清单式门禁最典型的
    /// 失效方式正是「加了东西忘了加进清单」，它安静得和「本来就合规」一模一样。
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllLayouts))]
    public void EveryPositionedFaceElement_IsEitherCheckedOrDeliberatelyExempt(string resourceFile)
    {
        var css = ReadEmbeddedTemplate(resourceFile);

        foreach (Match m in Regex.Matches(css, @"^\.([a-z0-9-]+)\s*\{([^}]*)\}", RegexOptions.Multiline))
        {
            var name = m.Groups[1].Value;
            if (!Regex.IsMatch(m.Groups[2].Value, @"(?:^|;)\s*top\s*:"))
                continue;   // 没有 top 就不是绝对定位的票面块

            (FaceElements.Contains(name) || Exempt.ContainsKey(name)).ShouldBeTrue(
                $"{resourceFile}: '.{name}' is positioned on the cheque face but this gate neither checks it "
                + $"nor exempts it. Add it to {nameof(FaceElements)} so its clearance is measured, or to "
                + $"{nameof(Exempt)} with the reason it cannot intrude on the amount field.");
        }
    }

    // ── 净空判定 ─────────────────────────────────────────────

    private readonly record struct Rect(decimal Left, decimal? Right, decimal Top, decimal? Bottom);

    /// <summary>金额区 = 框与美元符号的并集（净空是对两者一起要求的）。</summary>
    private static Rect AmountField(string css, string resourceFile)
    {
        var courtesy = Rule(css, resourceFile, "courtesy");
        var sign = Rule(css, resourceFile, "courtesy-sign");

        var left = PageWidthMm - Mm(courtesy, "right") - Mm(courtesy, "width");
        var top = Mm(courtesy, "top");
        return new Rect(
            Math.Min(left + Mm(sign, "left"), left),   // .courtesy-sign 的 left 是相对框的负偏移
            left + Mm(courtesy, "width"),
            top,
            top + Mm(courtesy, "height"));
    }

    /// <summary>
    /// 从声明块解析矩形。<c>Right</c>/<c>Bottom</c> 为 null = 由内容撑开、样式表答不出边缘。
    /// </summary>
    private static Rect? Box(string decls)
    {
        var top = MmOrNull(decls, "top");
        if (top == null)
            return null;

        var left = MmOrNull(decls, "left");
        var right = MmOrNull(decls, "right");
        var width = MmOrNull(decls, "width");
        var height = MmOrNull(decls, "height");

        // right: 定位的块以页右缘为锚；left: 定位的块没有 width 时右缘未知
        var absLeft = left ?? (right != null && width != null ? PageWidthMm - right.Value - width.Value : (decimal?)null);
        if (absLeft == null && right != null)
            absLeft = 0m;   // 只有 right、没有 width：左缘未知，按最坏情况张到页左
        if (absLeft == null)
            return null;

        var absRight = width != null ? absLeft + width : (right != null ? PageWidthMm - right : null);
        return new Rect(absLeft.Value, absRight, top.Value, height != null ? top + height : null);
    }

    /// <summary>
    /// 横向是否<b>确定</b>与净空带相交（只用声明出来的几何回答，不猜文本有多宽）。
    /// </summary>
    /// <remarks>
    /// ★ 右缘未知（左锚定、无 <c>width</c>）时<b>只看左缘在不在带内</b>，而不是按「伸到页外」的最坏
    /// 情况算。后者会把 <c>.pay-label</c>（左 10mm 的 6.5pt 小字）判成压在 200mm 处的金额框上。
    /// <para>
    /// ⚠️ <b>代价说清楚</b>：一段左锚定、无宽度的文本理论上可以长到伸进净空带，而本门禁看不见 ——
    /// 那正是「这行字有多宽」的问题，取决于字体与渲染器，模拟它只会制造虚假的精确感
    /// （本门禁的第一版就是为此拒绝模拟字体度量的）。缓解是结构性的：真正宽的那几个元素
    /// （<c>.legal</c> / <c>.payee</c> / <c>.date-block</c>）都声明了 <c>width</c>，因而被<b>精确</b>检查；
    /// 没声明宽度的一律是短标签，且它们的左缘离净空带都在 100mm 以上。
    /// </para>
    /// </remarks>
    private static bool DefinitelyOverlapsHorizontally(Rect e, Rect amount)
    {
        var bandLeft = amount.Left - RequiredClearanceMm;
        var bandRight = amount.Right!.Value + RequiredClearanceMm;

        if (e.Right == null)
            return e.Left > bandLeft && e.Left < bandRight;

        return e.Right.Value > bandLeft && e.Left < bandRight;
    }

    private static void AssertClear(Rect e, Rect amount, string name, string resourceFile)
    {
        if (!DefinitelyOverlapsHorizontally(e, amount))
            return;

        // 完全在金额区下方：元素的上缘就是它离金额区最近的边，与它多高无关
        if (e.Top >= amount.Bottom!.Value)
        {
            (e.Top - amount.Bottom.Value).ShouldBeGreaterThanOrEqualTo(RequiredClearanceMm,
                $"{resourceFile}: '.{name}' starts at {e.Top}mm while the amount field ends at "
                + $"{amount.Bottom}mm - CPA-006 5.4.3 requires {RequiredClearanceMm}mm of clear space below it. "
                + $"Move '.{name}' down, or move '.courtesy' up.");
            return;
        }

        // 在金额区上方（或与之交叠）：必须知道它的下缘，否则这条净空根本没被检查过
        e.Bottom.ShouldNotBeNull(
            $"{resourceFile}: '.{name}' sits above the amount field and overlaps it horizontally, but declares "
            + "no height - so where it ends cannot be computed from the stylesheet, and its clearance below is "
            + "unverifiable. Declare a height on it (see the note on '.courtesy').");

        (amount.Top - e.Bottom.Value).ShouldBeGreaterThanOrEqualTo(RequiredClearanceMm,
            $"{resourceFile}: '.{name}' ends at {e.Bottom}mm while the amount field starts at {amount.Top}mm - "
            + $"CPA-006 5.4.3 requires {RequiredClearanceMm}mm of clear space above it. "
            + $"Move '.{name}' up, or move '.courtesy' down.");
    }

    // ── 几何解析 ─────────────────────────────────────────────

    /// <summary>
    /// 大写金额行是否与金额框同处一行（按框的垂直跨度判断，不靠行高估算）。
    /// </summary>
    private static bool SharesRow(string legal, string courtesy)
    {
        var boxTop = Mm(courtesy, "top");
        var boxBottom = boxTop + Mm(courtesy, "height");
        var legalTop = Mm(legal, "top");
        return legalTop >= boxTop && legalTop <= boxBottom;
    }

    /// <summary>样式表里是否声明了某个 class 的规则（只认行首的规则，认不到注释里的名字）。</summary>
    private static bool HasRule(string css, string className)
        => RuleMatch(css, className).Success;

    private static Match RuleMatch(string css, string className)
        => Regex.Match(css, $@"^\.{Regex.Escape(className)}\s*\{{([^}}]*)\}}", RegexOptions.Multiline);

    /// <summary>取出某个 class 的样式声明块（找不到即红——门禁不许静默跳过）。</summary>
    private static string Rule(string css, string resourceFile, string className)
    {
        var match = RuleMatch(css, className);
        match.Success.ShouldBeTrue(
            $"{resourceFile}: no '.{className}' rule found. If the layout was restructured, this gate must be "
            + "updated with it - a gate that cannot find what it measures reports the same green as a clean sheet.");
        return match.Groups[1].Value;
    }

    /// <summary>取出声明块里某个毫米属性（缺失即红，理由同上）。</summary>
    private static decimal Mm(string declarations, string property)
    {
        var value = MmOrNull(declarations, property);
        value.ShouldNotBeNull($"no '{property}' in millimetres within: {declarations.Trim()}");
        return value.Value;
    }

    private static decimal? MmOrNull(string declarations, string property)
    {
        var match = Regex.Match(declarations, $@"(?:^|;)\s*{Regex.Escape(property)}\s*:\s*(-?[\d.]+)mm");
        return match.Success ? decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    private static string ReadEmbeddedTemplate(string resourceFile)
    {
        var assembly = typeof(CheckTemplates).Assembly;
        var name = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("Templates." + resourceFile, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
