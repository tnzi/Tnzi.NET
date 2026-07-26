namespace Tnzi.Finance.Documents.Services.Internal;

/// <summary>
/// <see cref="CheckRenderRequest"/> → <see cref="CheckDocumentModel"/>（模板绑定模型）
/// </summary>
/// <remarks>
/// 全部取值与格式化收口在此：模板只排版。含防篡改处理（金额数字前缀 <c>***</c>、
/// 金额大写用 <c>*</c> 填满行尾）与 MICR 拼装（复用 Finance 核心 internal 的
/// <c>MicrLineComposer</c>，经 InternalsVisibleTo 可见）。
/// </remarks>
internal static class CheckDocumentModelFactory
{
    /// <summary>金额大写行的目标字符宽度（不足部分以 <c>*</c> 填满，防止行尾被加写）。</summary>
    private const int LegalAmountWidth = 90;

    /// <summary>金额数字的防篡改前缀。</summary>
    private const string CourtesyAmountPrefix = "***";

    /// <summary>预印票纸下预印元素的 CSS class（屏幕可见、打印隐藏但保留占位）。</summary>
    private const string NoPrintClass = "noprint";

    public static CheckDocumentModel Create(CheckRenderRequest request)
    {
        Check.NotNull(request);

        var isPrePrinted = request.StockType == CheckStockType.PrePrinted;
        // MICR 只在白纸票纸现打；预印票纸上已印，重复打会让磁码读头拒读。
        var showMicr = !isPrePrinted && !string.IsNullOrWhiteSpace(request.AccountNumberPlain);

        return new CheckDocumentModel
        {
            IsPreview = request.IsPreview,
            PreviewLabel = "PREVIEW - NOT NEGOTIABLE",
            IsPrePrinted = isPrePrinted,
            PrePrintedClass = isPrePrinted ? NoPrintClass : string.Empty,
            ShowMicr = showMicr,
            OffsetStyle = BuildOffsetStyle(request.OffsetXMm, request.OffsetYMm),
            Issuer = request.Issuer ?? new CheckIssuerInfo(),
            Bank = new CheckBankView
            {
                Name = request.BankName,
                AccountName = request.AccountName,
                RoutingLine = BuildRoutingLine(request)
            },
            Checks = request.Checks.Select(item => CreateItem(request, item, showMicr)).ToList()
        };
    }

    private static CheckDocumentItem CreateItem(CheckRenderRequest request, CheckRenderItem item, bool showMicr)
    {
        var micrLine = showMicr
            ? MicrLineComposer.Compose(request.Scheme, item.CheckNumber, request.RoutingNumber,
                request.InstitutionNumber, request.TransitNumber, request.AccountNumberPlain!)
            : null;

        return new CheckDocumentItem
        {
            CheckNumberText = item.CheckNumber.ToString(CultureInfo.InvariantCulture),
            PayeeName = item.PayeeName,
            PayeeAddressLines = item.PayeeAddressLines,
            AmountText = CourtesyAmountPrefix + item.Amount.ToString("N2", CultureInfo.InvariantCulture),
            AmountInWordsText = FillLegalAmount(StripTrailingCurrencyWord(item.AmountInWords, item.Currency)),
            Currency = item.Currency,
            CurrencyLabel = CurrencyLabel(item.Currency),
            IssueDateText = item.IssueDate.ToString("yyyy MM dd", CultureInfo.InvariantCulture),
            IssueDateIso = item.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Memo = item.Memo,
            PaymentNumber = item.PaymentNumber,
            Reference = item.Reference,
            MicrLine = micrLine,
            MicrGlyphs = micrLine == null ? null : MicrLineComposer.ToFontGlyphs(micrLine)
        };
    }

    /// <summary>金额大写行尾以 <c>*</c> 填满到固定宽度，防止在空白处加写文字。</summary>
    private static string FillLegalAmount(string amountInWords)
    {
        var words = (amountInWords ?? string.Empty).Trim();
        if (words.Length >= LegalAmountWidth)
            return words;

        return words.Length == 0
            ? new string('*', LegalAmountWidth)
            : words + " " + new string('*', LegalAmountWidth - words.Length - 1);
    }

    /// <summary>
    /// 去掉 <c>CheckAmountInWords</c> 烘进大写串尾部的币种词（CAD/USD 为 "Dollars"，其余为 ISO 代码），
    /// 因为模板行尾另有一处预印的 <see cref="CurrencyLabel"/>（"DOLLARS"）；不去掉则币种字样重复两次。
    /// </summary>
    private static string StripTrailingCurrencyWord(string? amountInWords, string? currency)
    {
        var words = (amountInWords ?? string.Empty).TrimEnd();
        var label = CurrencyLabel(currency);
        if (words.Length > label.Length && words.EndsWith(label, StringComparison.OrdinalIgnoreCase))
            words = words[..^label.Length].TrimEnd();
        return words;
    }

    /// <summary>法定金额行尾的币种字样（模板单独排版的预印 "DOLLARS"）。</summary>
    private static string CurrencyLabel(string? currency)
    {
        var code = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
        return code is "USD" or "CAD" ? "DOLLARS" : code;
    }

    /// <summary>人可读的路由标识（票面银行区；机器可读的在 MICR 行）。</summary>
    private static string? BuildRoutingLine(CheckRenderRequest request)
        => request.Scheme switch
        {
            BankNumberScheme.CaEft when !string.IsNullOrWhiteSpace(request.TransitNumber)
                => $"Transit {request.TransitNumber!.Trim()} - Institution {request.InstitutionNumber?.Trim()}",
            BankNumberScheme.UsAba when !string.IsNullOrWhiteSpace(request.RoutingNumber)
                => $"Routing {request.RoutingNumber!.Trim()}",
            _ => null
        };

    /// <summary>全票面平移校准（预印票纸对齐用）。零偏移不产生 transform，避免多余的合成层。</summary>
    private static string BuildOffsetStyle(decimal offsetXMm, decimal offsetYMm)
        => offsetXMm == 0m && offsetYMm == 0m
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"transform: translate({offsetXMm}mm, {offsetYMm}mm);");
}
