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

    /// <summary>请求未指定时的不可流通标记文案。</summary>
    private const string DefaultPreviewLabel = "PREVIEW - NOT NEGOTIABLE";

    /// <param name="request">渲染请求（已含生效的版式 / 票纸 / 偏移 / 模板名）。</param>
    /// <param name="resolution">
    /// 已解析的模板与每页张数；null = 按请求自行解析一次（<see cref="BuiltInCheckTemplates.Resolve"/>）。
    /// 渲染器已经解析过，把结果传进来是为了让"模板名"与"每页张数"在一次渲染里只被决定一次。
    /// </param>
    public static CheckDocumentModel Create(CheckRenderRequest request, CheckTemplateResolution? resolution = null)
    {
        Check.NotNull(request);

        var effective = resolution ?? BuiltInCheckTemplates.Resolve(request.TemplateName, request.Layout);
        var isPrePrinted = request.StockType == CheckStockType.PrePrinted;
        // MICR 只在白纸票纸现打；预印票纸上已印，重复打会让磁码读头拒读。
        var showMicr = !isPrePrinted && !string.IsNullOrWhiteSpace(request.AccountNumberPlain);
        var items = request.Checks.Select(item => CreateItem(request, item, showMicr)).ToList();

        return new CheckDocumentModel
        {
            TemplateName = effective.TemplateName,
            Layout = request.Layout.ToString(),
            StockType = request.StockType.ToString(),
            ChecksPerPage = effective.ChecksPerPage,
            IsPreview = request.IsPreview,
            IsSpecimen = request.IsSpecimen,
            // 请求可指定文案（样张标 SPECIMEN，不然会被读成某笔真实付款的预览）；默认保持原样。
            PreviewLabel = string.IsNullOrWhiteSpace(request.PreviewLabel)
                ? DefaultPreviewLabel
                : request.PreviewLabel.Trim(),
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
            Checks = items,
            Pages = Paginate(items, effective.ChecksPerPage)
        };
    }

    /// <summary>按每页张数切页（最后一页可能不满）。</summary>
    private static List<CheckDocumentPage> Paginate(List<CheckDocumentItem> items, int checksPerPage)
    {
        // 目录给不出正数时按每页一张，绝不产生 0 或负数的步长（那会切出空页或死循环）。
        var perPage = checksPerPage < 1 ? 1 : checksPerPage;

        var pages = new List<CheckDocumentPage>();
        for (var i = 0; i < items.Count; i += perPage)
        {
            pages.Add(new CheckDocumentPage
            {
                Number = pages.Count + 1,
                Checks = items.GetRange(i, Math.Min(perPage, items.Count - i))
            });
        }
        return pages;
    }

    private static CheckDocumentItem CreateItem(CheckRenderRequest request, CheckRenderItem item, bool showMicr)
    {
        // ★ 票面的号与磁码行的串行号**同一个字符串**：算两次就是给它们两次分道扬镳的机会，
        // 而纸上写着一个号、读票机读出另一个，两边看起来都完全正常。
        var checkNumberText = CheckNumberFormat.Format(item.CheckNumber, request.CheckNumberDigits);

        var micrLine = showMicr
            ? MicrLineComposer.Compose(request.Scheme, checkNumberText, request.RoutingNumber,
                request.InstitutionNumber, request.TransitNumber, request.AccountNumberPlain!)
            : null;

        // 先剥掉大写串自带的币种词，两种法定金额行都从同一个"净"文本出发，
        // 于是无论调用方传进来的串带不带币种词，币种字样都恰好出现一次。
        var legalWords = StripTrailingCurrencyWord(item.AmountInWords, item.Currency);

        return new CheckDocumentItem
        {
            CheckNumberText = checkNumberText,
            PayeeName = item.PayeeName,
            PayeeAddressLines = item.PayeeAddressLines,
            AmountText = CourtesyAmountPrefix + item.Amount.ToString("N2", CultureInfo.InvariantCulture),
            AmountInWordsText = FillLegalAmount(legalWords),
            AmountInWordsWithCurrencyText = FillLegalAmount(AppendCurrencyWord(legalWords, item.Currency)),
            Currency = item.Currency,
            CurrencyLabel = CurrencyLabel(item.Currency),
            IssueDateText = item.IssueDate.ToString("yyyy MM dd", CultureInfo.InvariantCulture),
            IssueDateIso = item.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Memo = item.Memo,
            PaymentNumber = item.PaymentNumber,
            Reference = item.Reference,
            MicrLine = micrLine,
            MicrGlyphs = micrLine == null ? null : MicrLineComposer.ToFontGlyphs(micrLine),
            // 纯搬运：条数与长度已在银行域按物理空间归一化过，这里再限制一次只会产生
            // 两个都自称权威的上限。模板只负责把它们排出来。
            StubLines = item.StubLines
                .Select(line => new CheckDocumentStubLine { Label = line.Label, Value = line.Value })
                .ToList()
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

    /// <summary>
    /// 把币种词并进机打的大写金额（CPA-006 §5.4.1 第 9 条许可的另一种写法）。
    /// </summary>
    /// <remarks>
    /// ★ 给<b>大写金额与数字金额同处一行</b>的版式用（Figure C 开窗信封版）：那一行的右端
    /// 就是数字金额框，没有位置再单独放一个 "DOLLARS" —— 硬放进去会压掉规范要求的
    /// 0.64cm 净空，而屏幕预览看不出来，直接印成不合规的票寄出去。
    /// <para>
    /// ★ 币种词接在 <b><c>*</c> 填充之前</b>：接在后面等于在币种词与金额之间留出一段可加写的空白，
    /// 那正是填充要防的事。
    /// </para>
    /// <para>
    /// 用词复用 <see cref="CheckAmountInWords.CurrencySuffix"/>（USD/CAD → <c>Dollars</c>，
    /// 其余为 ISO 代码），与大写金额自带的收尾词同源。刻意不用
    /// <see cref="CurrencyLabel"/> 的全大写形态：那是给单独排版的预印元素用的，
    /// 并进句子里读起来像喊叫。
    /// </para>
    /// </remarks>
    private static string AppendCurrencyWord(string words, string? currency)
    {
        var word = CheckAmountInWords.CurrencySuffix(currency);
        return words.Length == 0 ? word : $"{words} {word}";
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
