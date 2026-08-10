namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// 收据字段的列宽与取值范围，以及越界时该怎么办。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么在核心而不在 <c>Tnzi.Finance.Ai</c></b>：<see cref="IReceiptExtractor"/> 是
/// <b>可替换的扩展点</b>，消费应用注册自己的实现是受支持的用法。校验写在某一个实现里，
/// 换一个实现就绕过去了 —— 而 <c>ReceiptCaptureService</c> 才是提取结果<b>跨进持久化</b>的
/// 那一道边界，是唯一不依赖「每个实现都记得做」的位置。
/// </para>
/// <para>
/// ★★ <b>机器给的值归一化，人打的值拒绝</b> —— 这条不对称是刻意的：
/// </para>
/// <list type="bullet">
/// <item>提取结果<b>没有人打过字</b>，整条丢掉会连带丢掉本来读对的字段；
/// 而它接下来就要交给人核对，归一化 + 记一条日志是代价最小的处理。</item>
/// <item>人手工填的值悄悄被改写是另一回事：他会看到自己没输入过的内容而没有任何解释。
/// 所以人工修正路径一律返回 400 并点名是哪个字段。</item>
/// </list>
/// <para>
/// ★ <b>超长的名字截断、超长的标识符丢弃</b>，判据是「错的值比空着更糟还是更好」：
/// <c>VendorName</c> 是给人看的、人会改，留开头比整条丢掉有用；而<b>币种与参考号会被
/// 下游拿去用</b>（建单取币种、银行规则按参考号匹配），截断出来的值看起来合法却是错的，
/// 那比空着危险得多。
/// </para>
/// <para>
/// 常量由 <c>ReceiptConfiguration</c> 直接引用，两处不可能漂移。
/// </para>
/// </remarks>
internal static class ReceiptFieldLimits
{
    /// <summary><see cref="Receipt.OriginalFileName"/> 列宽</summary>
    internal const int FileNameMaxLength = 256;

    /// <summary><see cref="Receipt.VendorName"/> 列宽</summary>
    internal const int VendorNameMaxLength = 256;

    /// <summary><see cref="Receipt.Currency"/> 列宽</summary>
    internal const int CurrencyMaxLength = 8;

    /// <summary><see cref="Receipt.Reference"/> 列宽</summary>
    internal const int ReferenceMaxLength = 128;

    /// <summary><see cref="Receipt.ConvertedDocType"/> 列宽</summary>
    internal const int ConvertedDocTypeMaxLength = 32;

    /// <summary><see cref="Receipt.FailReason"/> 列宽</summary>
    internal const int FailReasonMaxLength = 512;

    /// <summary>
    /// 行项条数上限。
    /// </summary>
    /// <remarks>
    /// 行项是<b>供人核对</b>的附带信息（合计另有字段），这条上限只防病态输出把几 MB JSON
    /// 塞进一列，不是业务约束 —— 最长的超市小票也远在此之下。
    /// </remarks>
    internal const int LineItemsMaxCount = 500;

    /// <summary>金额列 <c>decimal(19,4)</c> 能表达的上界。</summary>
    /// <remarks>超出即插入失败，而「提取成功却存不进去」会把 500 报在最不该报的地方。</remarks>
    internal const decimal MoneyMax = 999_999_999_999_999.9999m;

    /// <summary>
    /// 校验人工填写的字符串字段。返回 <see langword="null"/> 表示通过，否则是给用户看的消息。
    /// </summary>
    internal static string? ValidateUserInput(string? fileName, string? vendorName, string? currency, string? reference)
    {
        if (fileName != null && fileName.Length > FileNameMaxLength)
            return $"The file name must be at most {FileNameMaxLength} characters.";
        if (vendorName != null && vendorName.Length > VendorNameMaxLength)
            return $"The vendor name must be at most {VendorNameMaxLength} characters.";
        if (reference != null && reference.Length > ReferenceMaxLength)
            return $"The reference must be at most {ReferenceMaxLength} characters.";
        if (!string.IsNullOrWhiteSpace(currency) && !IsCurrencyCode(currency))
            return "The currency must be a 3-letter ISO 4217 code, for example CAD.";
        return null;
    }

    /// <summary>
    /// 把提取器交回来的结果收敛到实体能装下、界面能读懂的形状。
    /// </summary>
    /// <param name="raw">提取器原样返回的结果（不被修改）。</param>
    /// <param name="adjustments">
    /// 发生过的调整，供调用方记一条日志。空表示原样可用。
    /// </param>
    /// <returns>归一化后的<b>新</b>对象。</returns>
    internal static ReceiptExtractionResult NormalizeExtraction(
        ReceiptExtractionResult raw, out IReadOnlyList<string> adjustments)
    {
        Check.NotNull(raw);
        var notes = new List<string>();
        adjustments = notes;

        var lineItems = raw.LineItems ?? [];
        if (lineItems.Count > LineItemsMaxCount)
        {
            notes.Add($"line items truncated from {lineItems.Count} to {LineItemsMaxCount}");
            lineItems = [.. lineItems.Take(LineItemsMaxCount)];
        }

        return new ReceiptExtractionResult
        {
            VendorName = Truncate(raw.VendorName, VendorNameMaxLength, nameof(raw.VendorName), notes),
            DocDate = raw.DocDate,
            Currency = NormalizeCurrency(raw.Currency, notes),
            Subtotal = ClampMoney(raw.Subtotal, nameof(raw.Subtotal), notes),
            TaxAmount = ClampMoney(raw.TaxAmount, nameof(raw.TaxAmount), notes),
            Total = ClampMoney(raw.Total, nameof(raw.Total), notes),
            Reference = DropIfTooLong(raw.Reference, ReferenceMaxLength, nameof(raw.Reference), notes),
            LineItems = [.. lineItems],
            Confidence = NormalizeConfidence(raw.Confidence, notes),
            RawText = raw.RawText,
        };
    }

    /// <summary>把提取失败原因收敛到 <see cref="Receipt.FailReason"/> 装得下的长度。</summary>
    /// <remarks>
    /// ★ 失败路径是最不能再失败一次的地方：提取器（或它背后的模型服务）的错误消息动辄
    /// 上千字符，原样赋值会让「记下失败原因」这一步自己抛出插入异常，
    /// 于是收据既没有结果也没有失败记录，重试按钮无从出现。
    /// </remarks>
    internal static string TruncateFailReason(string? reason)
    {
        var text = string.IsNullOrWhiteSpace(reason) ? "Extraction failed." : reason.Trim();
        return Cut(text, FailReasonMaxLength);
    }

    /// <summary>
    /// 按<b>列宽</b>（UTF-16 码元数）截断，且不留下半个代理对。
    /// </summary>
    /// <remarks>
    /// ★ 不能用框架的 <c>TruncateByTextElements</c>：那个按**字素簇**计数，而列宽约束是码元数，
    /// 一个 emoji 就能让「截到 256 个字素」仍然超过 <c>varchar(256)</c>。
    /// <para>
    /// ★ 但裸 <c>text[..max]</c> 可能正好切在代理对中间，留下一个孤立的高代理项 ——
    /// 那是一个非法的 UTF-16 串，PostgreSQL 在编码成 UTF-8 时会**直接拒绝整条插入**。
    /// 一个防「写库失败」的类自己造出写库失败，就没有意义了。
    /// </para>
    /// </remarks>
    private static string Cut(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        var end = maxLength;
        if (char.IsHighSurrogate(text[end - 1]))
            end--;

        return text[..end];
    }

    /// <summary>三个 ASCII 字母即视为币种代码。</summary>
    /// <remarks>
    /// 刻意不查 ISO 4217 全表：那张表随年份变（新币种、退役币种），框架把它钉死只会让
    /// 用了合法新币种的部署被自己的框架拦住。形状校验足以挡住「US Dollars」「$」这类
    /// 一定会在下游建单时炸掉的值。
    /// </remarks>
    internal static bool IsCurrencyCode(string? value)
    {
        var text = value?.Trim();
        if (text is not { Length: 3 })
            return false;
        foreach (var c in text)
        {
            if (!char.IsAsciiLetter(c))
                return false;
        }
        return true;
    }

    /// <summary>
    /// 置信度归一化到 0-1。
    /// </summary>
    /// <remarks>
    /// ★ 模型答 <c>95</c> 而不是 <c>0.95</c> 是常见行为（prompt 写着 0 到 1 也一样），
    /// 而呈现端一律按 <c>value * 100</c> 渲染百分比 —— 于是界面上写着 <b>9500%</b>。
    /// 落在 (1, 100] 的值按百分比还原是唯一说得通的读法；1 本身在两种读法下都是满值。
    /// <para>
    /// ★ 超出 (0, 100] 的值<b>没有</b>可还原的含义，归 0 而不是钳到 1：0 的意思是
    /// 「这条需要人看」，而钳到 1 等于告诉操作员「完全可信」—— 两个方向的代价不对称。
    /// </para>
    /// </remarks>
    internal static decimal NormalizeConfidence(decimal raw, List<string> notes)
    {
        switch (raw)
        {
            case >= 0m and <= 1m:
                return raw;
            case > 1m and <= 100m:
                notes.Add($"confidence {raw} read as a percentage");
                return raw / 100m;
            default:
                notes.Add($"confidence {raw} is out of range and was treated as unknown");
                return 0m;
        }
    }

    private static string? NormalizeCurrency(string? raw, List<string> notes)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var text = raw.Trim().ToUpperInvariant();
        if (IsCurrencyCode(text))
            return text;

        notes.Add($"currency '{Preview(text)}' is not a 3-letter code and was dropped");
        return null;
    }

    private static decimal? ClampMoney(decimal? raw, string field, List<string> notes)
    {
        if (raw == null)
            return null;
        if (Math.Abs(raw.Value) <= MoneyMax)
            return raw;

        notes.Add($"{field} {raw} exceeds the money column range and was dropped");
        return null;
    }

    private static string? Truncate(string? raw, int maxLength, string field, List<string> notes)
    {
        if (raw == null || raw.Length <= maxLength)
            return raw;

        notes.Add($"{field} truncated from {raw.Length} to {maxLength} characters");
        return Cut(raw, maxLength);
    }

    private static string? DropIfTooLong(string? raw, int maxLength, string field, List<string> notes)
    {
        if (raw == null || raw.Length <= maxLength)
            return raw;

        notes.Add($"{field} is {raw.Length} characters, past the {maxLength} limit, and was dropped");
        return null;
    }

    /// <summary>日志里只放前 32 个字符：越界的值本来就可能是一整段文本。</summary>
    private static string Preview(string text)
        => text.Length <= 32 ? text : text[..32] + "...";
}
