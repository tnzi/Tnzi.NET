namespace Tnzi.Finance.Tests;

/// <summary>
/// 收据字段边界的判定表。
/// </summary>
/// <remarks>
/// <para>
/// 提取器是<b>可替换的扩展点</b>，它交回来的东西必须当外部数据看待。这里锁的是
/// <see cref="ReceiptFieldLimits"/> 的三类处理各自的<b>方向</b>——方向错了不会有人看出来：
/// </para>
/// <list type="bullet">
/// <item>置信度落在 (1, 100] 按百分比还原（模型答 95 而不是 0.95 是常见行为，
/// 而呈现端一律 <c>×100</c> 渲染，界面上会写着 <b>9500%</b>）。</item>
/// <item>更离谱的置信度归 <b>0 而不是 1</b>：0 的意思是「这条需要人看」，
/// 钳到 1 等于告诉操作员「完全可信」。</item>
/// <item>超长的<b>名字</b>截断（人会改，留开头有用），超长的<b>标识符</b>丢弃
/// （币种与参考号会被下游拿去建单、拿去匹配，截出来的值看起来合法却是错的）。</item>
/// </list>
/// <para>
/// ⚠️ 这些是<b>纯函数</b>测试，只能证明判定表本身对；「服务真的调了它」由
/// <c>Integration/ReceiptCaptureTests</c> 走真实入口的用例负责。
/// </para>
/// </remarks>
public class ReceiptFieldLimitsTests
{
    // ── 置信度 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(0.42, 0.42)]
    public void Confidence_already_in_range_is_left_alone(decimal raw, decimal expected)
    {
        var notes = new List<string>();

        ReceiptFieldLimits.NormalizeConfidence(raw, notes).ShouldBe(expected);
        notes.ShouldBeEmpty();
    }

    /// <summary>
    /// 模型按百分比作答是常见行为；(1, 100] 按百分比还原是唯一说得通的读法。
    /// </summary>
    [Theory]
    [InlineData(95, 0.95)]
    [InlineData(100, 1)]
    [InlineData(2, 0.02)]
    public void Confidence_above_one_is_read_as_a_percentage(decimal raw, decimal expected)
    {
        var notes = new List<string>();

        ReceiptFieldLimits.NormalizeConfidence(raw, notes).ShouldBe(expected);
        notes.ShouldNotBeEmpty();
    }

    /// <summary>
    /// 完全没有可还原含义的值归 <b>0</b>（=需要人看），而不是钳到 1（=完全可信）。
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(9999)]
    public void Confidence_that_cannot_be_recovered_becomes_zero_not_one(decimal raw)
    {
        var notes = new List<string>();

        ReceiptFieldLimits.NormalizeConfidence(raw, notes).ShouldBe(0m);
        notes.ShouldNotBeEmpty();
    }

    // ── 字符串：截断 vs 丢弃 ──────────────────────────────────────────────────

    [Fact]
    public void Over_long_vendor_name_is_truncated_because_a_person_will_fix_it()
    {
        var raw = Result(vendorName: new string('A', 400));

        var normalized = ReceiptFieldLimits.NormalizeExtraction(raw, out var adjustments);

        normalized.VendorName!.Length.ShouldBe(ReceiptFieldLimits.VendorNameMaxLength);
        adjustments.ShouldNotBeEmpty();
    }

    /// <summary>
    /// 参考号超长<b>丢弃而不是截断</b>：银行规则会按参考号匹配，
    /// 截一半的号看起来合法却会配到别的东西上。
    /// </summary>
    [Fact]
    public void Over_long_reference_is_dropped_not_truncated()
    {
        var raw = Result(reference: new string('9', 300));

        var normalized = ReceiptFieldLimits.NormalizeExtraction(raw, out var adjustments);

        normalized.Reference.ShouldBeNull();
        adjustments.ShouldNotBeEmpty();
    }

    /// <summary>不是 3 字母代码的币种一律丢弃（下游建单会拿它去查币种）。</summary>
    [Theory]
    [InlineData("US Dollars")]
    [InlineData("$")]
    [InlineData("CADX")]
    [InlineData("C4D")]
    public void Currency_that_is_not_a_three_letter_code_is_dropped(string raw)
    {
        var normalized = ReceiptFieldLimits.NormalizeExtraction(Result(currency: raw), out var adjustments);

        normalized.Currency.ShouldBeNull();
        adjustments.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("cad", "CAD")]
    [InlineData("  usd  ", "USD")]
    public void Currency_that_is_a_three_letter_code_is_normalized_to_upper_case(string raw, string expected)
    {
        var normalized = ReceiptFieldLimits.NormalizeExtraction(Result(currency: raw), out var adjustments);

        normalized.Currency.ShouldBe(expected);
        adjustments.ShouldBeEmpty();
    }

    // ── 金额与行项 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 超出 <c>decimal(19,4)</c> 的金额丢弃：留着它会让「提取成功」这一步自己插入失败。
    /// </summary>
    [Fact]
    public void Money_beyond_the_column_range_is_dropped()
    {
        var raw = Result();
        raw.Total = ReceiptFieldLimits.MoneyMax + 1m;
        raw.Subtotal = 12.34m;

        var normalized = ReceiptFieldLimits.NormalizeExtraction(raw, out var adjustments);

        normalized.Total.ShouldBeNull();
        normalized.Subtotal.ShouldBe(12.34m);   // 对照：范围内的金额不许被牵连
        adjustments.ShouldNotBeEmpty();
    }

    [Fact]
    public void Line_items_are_capped()
    {
        var raw = Result();
        raw.LineItems = [.. Enumerable.Range(0, ReceiptFieldLimits.LineItemsMaxCount + 10)
            .Select(i => new ReceiptExtractionLineItem { Description = $"item {i}", Amount = 1m })];

        var normalized = ReceiptFieldLimits.NormalizeExtraction(raw, out var adjustments);

        normalized.LineItems.Count.ShouldBe(ReceiptFieldLimits.LineItemsMaxCount);
        adjustments.ShouldNotBeEmpty();
    }

    // ── 原样通过 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 一份规规矩矩的提取结果<b>逐字段原样通过</b>，且不产生任何调整记录。
    /// </summary>
    /// <remarks>
    /// 这条是全文件的对照：没有它，把归一化写成「什么都丢掉」也一样绿。
    /// </remarks>
    [Fact]
    public void A_well_formed_result_passes_through_unchanged()
    {
        var raw = new ReceiptExtractionResult
        {
            VendorName = "Acme Supplies",
            DocDate = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc),
            Currency = "CAD",
            Subtotal = 90m,
            TaxAmount = 11.7m,
            Total = 101.7m,
            Reference = "INV-42",
            Confidence = 0.87m,
            RawText = "ACME SUPPLIES ...",
            LineItems = [new ReceiptExtractionLineItem { Description = "Widget", Quantity = 2, Amount = 90m }],
        };

        var normalized = ReceiptFieldLimits.NormalizeExtraction(raw, out var adjustments);

        adjustments.ShouldBeEmpty();
        normalized.VendorName.ShouldBe("Acme Supplies");
        normalized.DocDate.ShouldBe(raw.DocDate);
        normalized.Currency.ShouldBe("CAD");
        normalized.Subtotal.ShouldBe(90m);
        normalized.TaxAmount.ShouldBe(11.7m);
        normalized.Total.ShouldBe(101.7m);
        normalized.Reference.ShouldBe("INV-42");
        normalized.Confidence.ShouldBe(0.87m);
        normalized.RawText.ShouldBe("ACME SUPPLIES ...");
        normalized.LineItems.Count.ShouldBe(1);
    }

    /// <summary>归一化返回<b>新对象</b>，不改写提取器交给我们的那一个。</summary>
    [Fact]
    public void Normalization_does_not_mutate_the_input()
    {
        var raw = Result(vendorName: new string('A', 400));
        raw.Confidence = 95m;

        ReceiptFieldLimits.NormalizeExtraction(raw, out _);

        raw.VendorName!.Length.ShouldBe(400);
        raw.Confidence.ShouldBe(95m);
    }

    // ── 失败原因 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 失败原因必须收敛到列宽：失败路径是最不能再失败一次的地方。
    /// </summary>
    [Fact]
    public void Fail_reason_is_truncated_to_the_column_width()
    {
        ReceiptFieldLimits.TruncateFailReason(new string('x', 5000))
            .Length.ShouldBe(ReceiptFieldLimits.FailReasonMaxLength);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_fail_reason_becomes_a_readable_default(string? reason)
    {
        ReceiptFieldLimits.TruncateFailReason(reason).ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// 截断不得留下半个代理对 —— 那是非法 UTF-16，PostgreSQL 编码成 UTF-8 时会拒绝整条插入。
    /// </summary>
    /// <remarks>
    /// 一个防「写库失败」的类自己造出写库失败就没有意义了。用 emoji（每个占 2 个 UTF-16 码元）
    /// 拼到恰好让边界落在代理对中间。
    /// </remarks>
    [Fact]
    public void Truncation_never_leaves_half_a_surrogate_pair()
    {
        // 512 个码元 = 256 个 emoji；再往前塞一个奇数长度的前缀，让第 512 个码元成为高代理项
        var text = "x" + string.Concat(Enumerable.Repeat("😀", 400));

        var cut = ReceiptFieldLimits.TruncateFailReason(text);

        cut.Length.ShouldBeLessThanOrEqualTo(ReceiptFieldLimits.FailReasonMaxLength);
        char.IsHighSurrogate(cut[^1]).ShouldBeFalse("截断留下了一个孤立的高代理项");
        // 对照：仍然截到了尽可能长（只少一个码元）
        cut.Length.ShouldBeGreaterThanOrEqualTo(ReceiptFieldLimits.FailReasonMaxLength - 1);
    }

    /// <summary>名字截断走同一段逻辑，同样不留半个代理对。</summary>
    [Fact]
    public void Vendor_name_truncation_never_leaves_half_a_surrogate_pair()
    {
        var raw = Result(vendorName: "x" + string.Concat(Enumerable.Repeat("😀", 200)));

        var normalized = ReceiptFieldLimits.NormalizeExtraction(raw, out _);

        var name = normalized.VendorName!;
        name.Length.ShouldBeLessThanOrEqualTo(ReceiptFieldLimits.VendorNameMaxLength);
        char.IsHighSurrogate(name[^1]).ShouldBeFalse("截断留下了一个孤立的高代理项");
    }

    // ── 人工输入 ──────────────────────────────────────────────────────────────

    [Fact]
    public void User_input_within_the_limits_is_accepted()
    {
        ReceiptFieldLimits.ValidateUserInput("receipt.pdf", "Acme Supplies", "cad", "INV-42").ShouldBeNull();
    }

    [Theory]
    [InlineData("vendor")]
    [InlineData("currency")]
    [InlineData("reference")]
    [InlineData("fileName")]
    public void User_input_past_a_limit_is_rejected_with_the_field_named(string field)
    {
        var error = field switch
        {
            "vendor" => ReceiptFieldLimits.ValidateUserInput(null, new string('A', 400), null, null),
            "currency" => ReceiptFieldLimits.ValidateUserInput(null, null, "US Dollars", null),
            "reference" => ReceiptFieldLimits.ValidateUserInput(null, null, null, new string('9', 300)),
            _ => ReceiptFieldLimits.ValidateUserInput(new string('f', 400), null, null, null),
        };

        error.ShouldNotBeNull();
    }

    /// <summary>空币种不是错误（可选字段）。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Blank_currency_is_not_an_error(string? currency)
    {
        ReceiptFieldLimits.ValidateUserInput(null, null, currency, null).ShouldBeNull();
    }

    private static ReceiptExtractionResult Result(
        string? vendorName = "Acme", string? currency = "CAD", string? reference = "INV-1")
        => new()
        {
            VendorName = vendorName,
            Currency = currency,
            Reference = reference,
            Confidence = 0.8m,
        };
}
