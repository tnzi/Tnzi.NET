using System.Security.Cryptography;

namespace Tnzi.Signing.Services.Internal;

/// <summary>密封结果：成品文件 id 与它的哈希锚点。</summary>
public sealed record SealResult(Guid FileId, string Sha256, string FileName);

/// <summary>预填结果：烧好值的渲染稿（没有可烧的值时 <see cref="FileId"/> 为 null）与烧进去的字段键。</summary>
public sealed record PrefillResult(Guid? FileId, IReadOnlyList<string> Keys);

/// <summary>密封预检结果：文档的真实页数，以及锚文本在文档里找不到的字段键。</summary>
public sealed record SealableDocument(int PageCount, IReadOnlyList<string> UnfoundAnchorKeys);

/// <summary>
/// 把收集齐的字段值盖到渲染稿上、压平、算哈希、存成成品。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>哈希在密封那一刻算，且只算一次。</b>它与完成证书一起构成"这份 PDF 就是当初签的那份"
/// 的证据链；任何"重新生成一遍"的路径都会让它失去意义，所以密封是<b>一次性</b>动作。
/// 并发下的唯一性由调用方在密封**之前**用乐观并发戳抢占（<c>EnvelopeService.TryClaimSealAsync</c>）
/// —— 状态机本身保证不了这件事：并行签署时两位签署人会同时读到"全签完"。
/// 下面那道 <see cref="Envelope.FinalPdfFileId"/> 的检查是本地兜底，挡的是"拿一份已经密封过的
/// 请求再调一次"，挡不住真正的并发（两边手上的副本都还是空的）。
/// </para>
/// <para>
/// 盖章走 <c>IPdfStamper</c>：内容直接画进页面内容层，输出天然压平 —— 刻意不使用 AcroForm 表单域，
/// 那种做法留下的是"还能再改"的文档。
/// </para>
/// </remarks>
public sealed class SigningSealer
{
    private readonly IPdfStamper _stamper;
    private readonly IPdfInspector _inspector;
    private readonly IFileStorageService _files;
    private readonly ILogger<SigningSealer> _logger;

    public SigningSealer(
        IPdfStamper stamper,
        IPdfInspector inspector,
        IFileStorageService files,
        ILogger<SigningSealer> logger)
    {
        _stamper = Check.NotNull(stamper);
        _inspector = Check.NotNull(inspector);
        _files = Check.NotNull(files);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 发出前把发起方负责的字段值（合并变量 / 预填）烧进一份本信封自有的渲染稿。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 签署人在签的时候要看得见将被密封的内容。收件人载荷只含本人角色的字段，发起方字段不在其中；
    /// 若这些值等到密封才盖，签署人签的是一份价格处空白的文档，成品上却有价格 —— 哈希与证书据此生成。
    /// 所以发出那一刻就把它们盖到一份新文件上，快照记下烧进去的键（<see cref="SigningSnapshot.PrefilledKeys"/>），
    /// 密封时跳过。没有任何可烧的值时不生成文件（<see cref="PrefillResult.FileId"/> 为 null），信封照旧引用模板的渲染稿。
    /// </para>
    /// </remarks>
    public async Task<Result<PrefillResult>> PrefillAsync(
        Envelope request,
        SigningSnapshot snapshot,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);
        Check.NotNull(snapshot);
        Check.NotNull(values);

        var senderFields = snapshot.Fields
            .Where(f => !f.IsSignatureLike && string.IsNullOrWhiteSpace(f.RecipientRole))
            .Where(f => !string.IsNullOrEmpty(values.GetValueOrDefault(f.Key)))
            .ToList();
        if (senderFields.Count == 0)
            return Result<PrefillResult>.Success(new PrefillResult(null, []));

        if (request.RenderedPdfFileId is not { } renderedId)
            return Result<PrefillResult>.Failure("This request has no rendered document to prefill.", 409);

        var source = await ReadAsync(renderedId, cancellationToken);
        if (source is null)
            return Result<PrefillResult>.Failure("The rendered document could not be read.", 404);

        var pages = ReadPageGeometry(source);
        var stamps = new List<PdfStamp>();
        foreach (var field in senderFields)
        {
            if (ResolvePlacement(source, field) is not { } placement)
                continue;
            stamps.AddRange(TextStamps(placement.Page, placement.Rect, field, values[field.Key]!, pages));
        }

        byte[] prefilled;
        try
        {
            prefilled = _stamper.Stamp(source, new PdfStampRequest { Stamps = stamps });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Prefilling signing request {RequestId} failed while stamping.", request.Id);
            return Result<PrefillResult>.Failure("The document could not be rendered.", 500);
        }

        using var output = new MemoryStream(prefilled, writable: false);
        var saved = await _files.SaveAsync(BuildFileName(request.Title, "-draft"), output);
        if (!saved.Succeeded || saved.Data is null)
            return Result<PrefillResult>.Failure("The rendered document could not be stored.", 500);

        // 记的是「盖了哪些键」而不是「盖成功了几个」：找不到锚的字段也算烧过 ——
        // 密封时再搜一遍同样找不到，只会多一行同样的 Warning。
        return Result<PrefillResult>.Success(new PrefillResult(saved.Data.Id, senderFields.Select(f => f.Key).ToList()));
    }

    /// <summary>
    /// 密封一份请求：取渲染稿 → 盖上所有值与签名 → 压平 → 算 SHA-256 → 存文件。
    /// </summary>
    public async Task<Result<SealResult>> SealAsync(
        Envelope request,
        SigningSnapshot snapshot,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyList<Signer> recipients,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);
        Check.NotNull(snapshot);

        if (request.FinalPdfFileId is not null)
            return Result<SealResult>.Failure("This request has already been sealed.", 409);

        if (request.RenderedPdfFileId is not { } renderedId)
            return Result<SealResult>.Failure("This request has no rendered document to seal.", 409);

        var source = await ReadAsync(renderedId, cancellationToken);
        if (source is null)
            return Result<SealResult>.Failure("The rendered document could not be read.", 404);

        var stamps = BuildStamps(source, snapshot, values, recipients);

        byte[] sealed_;
        try
        {
            sealed_ = _stamper.Stamp(source, new PdfStampRequest { Stamps = stamps });
        }
        catch (Exception ex)
        {
            // 盖章失败不能让请求停在一个说不清的中间态：调用方据此保持原状态并可重试。
            _logger.LogError(ex, "Sealing signing request {RequestId} failed while stamping.", request.Id);
            return Result<SealResult>.Failure("The document could not be sealed.", 500);
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(sealed_));
        var fileName = SealedFileName(request.Title);

        using var output = new MemoryStream(sealed_, writable: false);
        var saved = await _files.SaveAsync(fileName, output);
        if (!saved.Succeeded || saved.Data is null)
            return Result<SealResult>.Failure("The sealed document could not be stored.", 500);

        return Result<SealResult>.Success(new SealResult(saved.Data.Id, hash, fileName));
    }

    private async Task<byte[]?> ReadAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var pdfResult = await _files.GetAsync(fileId);
        if (!pdfResult.Succeeded || pdfResult.Data is null)
            return null;

        await using var stream = pdfResult.Data;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>把每个字段的取值翻译成一次盖章。发出时已烧进渲染稿的键跳过。</summary>
    private List<PdfStamp> BuildStamps(
        byte[] source,
        SigningSnapshot snapshot,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyList<Signer> recipients)
    {
        var stamps = new List<PdfStamp>();
        var pages = ReadPageGeometry(source);

        // 签名图按角色取：字段声明的是"谁来签"，落笔的是那个人交上来的图。
        var signatureByRole = recipients
            .Where(r => !string.IsNullOrEmpty(r.SignatureImage) && !string.IsNullOrWhiteSpace(r.Role))
            .GroupBy(r => r.Role, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().SignatureImage!, StringComparer.OrdinalIgnoreCase);

        var prefilled = new HashSet<string>(snapshot.PrefilledKeys, StringComparer.Ordinal);

        foreach (var field in snapshot.Fields)
        {
            // 发出时已经在纸面上了（PrefillAsync）；再盖一次就是双重盖章。
            if (prefilled.Contains(field.Key)) continue;

            if (ResolvePlacement(source, field) is not { } placement) continue;
            var (page, target) = placement;

            if (field.IsSignatureLike)
            {
                if (field.RecipientRole is { Length: > 0 } role && signatureByRole.TryGetValue(role, out var image))
                {
                    stamps.Add(new PdfImageStamp
                    {
                        PageNumber = page,
                        Rect = target,
                        DataUrl = image,
                        PreserveAspectRatio = true,
                    });
                }
                else
                {
                    // 一个签名字段没有图可盖：角色为空（建模板那侧现在拦了，旧快照不经那道门），
                    // 或者那个角色的人没有交图。成品会少一个签名而外观完整，这一行是唯一的症状。
                    _logger.LogWarning(
                        "Signature field {FieldKey} (role {Role}) has no signature image to stamp; the field was left blank.",
                        field.Key, field.RecipientRole ?? "<none>");
                }
                continue;
            }

            if (!values.TryGetValue(field.Key, out var value) || string.IsNullOrEmpty(value))
                continue;

            stamps.AddRange(TextStamps(page, target, field, value, pages));
        }

        return stamps;
    }

    /// <summary>字段没有指定字号时的字号（pt）。</summary>
    private const double DefaultFontSize = 10d;

    /// <summary>为了装进字段框而缩小字号时的下限（pt）：再小就读不出来了，宁可让多出的行越出框底。</summary>
    private const double MinFontSize = 6d;

    /// <summary>多行时的行距（字号的倍数）。</summary>
    private const double LineSpacing = 1.2d;

    /// <summary>每一步缩小多少（pt）。</summary>
    private const double FontSizeStep = 0.5d;

    /// <summary>
    /// 一个文本字段的取值 → 一次或多次文本盖章：放得下就是框内一行，放不下就按框宽换行、必要时缩小字号。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <see cref="IPdfStamper"/> 不换行也不裁剪，放不下的部分照样画出去：一段 150 字的地址在一个 268 pt 宽的框里
    /// 会单行穿过相邻内容、越过页边，而 <c>FieldValue</c> 里存的是全文，密封的成品与记录下来的值对不上。
    /// 值里自带的换行也没人管。所以这里按实测宽度（<see cref="PdfTextFitter"/>，与画字的是同一个实现）换行，
    /// 行数乘行距超过框高时逐步缩小字号（字段字号是上限，不小于 <see cref="MinFontSize"/>），各行自框顶向下排。
    /// 缩到下限仍放不下时照样全部画出（多出的行越出框底）：丢字比越界更糟，成品要能读出完整的值。
    /// </para>
    /// <para>
    /// 一行放得下的值（绝大多数）保持原样：框内垂直居中靠左、字段字号，与此前的成品逐字一致。
    /// 勾选框只画一个字符，不参与换行。读不出页面尺寸或量不了宽度（没有可用字体）时也退回单行，
    /// 后者随后在盖章那一步以同一个原因失败，由调用方的失败处理接住。
    /// </para>
    /// </remarks>
    private IReadOnlyList<PdfTextStamp> TextStamps(
        int page, NormalizedRect target, SnapshotField field, string value, IReadOnlyList<PdfPageInfo>? pages)
    {
        var fontSize = field.FontSize is > 0 ? (double)field.FontSize.Value : DefaultFontSize;
        if (field.Type == SigningFieldType.Checkbox)
            return [Line(page, target, RenderCheckbox(value), fontSize)];

        var geometry = pages?.FirstOrDefault(p => p.Number == page);
        if (geometry is not { Width: > 0, Height: > 0 })
            return [Line(page, target, value, fontSize)];

        // 宽为 0 = 画到页面右边缘（与盖章实现的换算同一口径）；高为 0 = 不限高，只换行不缩小。
        var boxWidth = (target.Width > 0 ? target.Width : 1d - target.X) * geometry.Width;
        var boxHeight = target.Height > 0 ? target.Height * geometry.Height : double.PositiveInfinity;
        if (boxWidth <= 0)
            return [Line(page, target, value, fontSize)];

        IReadOnlyList<string> lines;
        try
        {
            var paragraphs = value.ReplaceLineEndings("\n").Split('\n');
            if (paragraphs.Length == 1 && new PdfTextFitter(_stamper, fontSize).Fits(value, boxWidth))
                return [Line(page, target, value, fontSize)];

            while (true)
            {
                var fitter = new PdfTextFitter(_stamper, fontSize);
                lines = paragraphs.SelectMany(p => fitter.Wrap(p, boxWidth)).ToList();
                if (lines.Count * fontSize * LineSpacing <= boxHeight || fontSize <= MinFontSize)
                    break;
                fontSize = Math.Max(MinFontSize, fontSize - FontSizeStep);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Field {FieldKey} could not be measured for wrapping; it is stamped as a single line.", field.Key);
            return [Line(page, target, value, fontSize)];
        }

        // 缩小之后一行就放下了：仍按单行的样子放在框内。
        if (lines.Count == 1)
            return [Line(page, target, lines[0], fontSize)];

        var lineHeight = fontSize * LineSpacing / geometry.Height;
        var stamps = new List<PdfTextStamp>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Length == 0) continue;
            var rect = new NormalizedRect(target.X, target.Y + (i * lineHeight), target.Width, lineHeight);
            stamps.Add(Line(page, rect, lines[i], fontSize));
        }

        return stamps;
    }

    private static PdfTextStamp Line(int page, NormalizedRect rect, string text, double fontSize) => new()
    {
        PageNumber = page,
        Rect = rect,
        Text = text,
        FontSize = fontSize,
        Alignment = PdfStampAlignment.CenterLeft,
    };

    /// <summary>每页的尺寸（pt），换行要用；读不出时为 null（文本字段退回单行，读不出的文档随后在盖章那一步失败）。</summary>
    private IReadOnlyList<PdfPageInfo>? ReadPageGeometry(byte[] source)
    {
        try
        {
            return _inspector.GetInfo(source)?.Pages;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The page sizes of a document being stamped could not be read; text fields are stamped as single lines.");
            return null;
        }
    }

    /// <summary>
    /// 解析字段落点（页码 + 矩形）。<see cref="FieldPlacementMode.Anchor"/> 时按锚文本现搜。
    /// </summary>
    private (int Page, NormalizedRect Rect)? ResolvePlacement(byte[] source, SnapshotField field)
    {
        if (field.PlacementMode == FieldPlacementMode.Absolute)
        {
            return (field.Page,
                new NormalizedRect((double)field.X, (double)field.Y, (double)field.W, (double)field.H));
        }

        if (string.IsNullOrWhiteSpace(field.AnchorText))
            return null;

        var hit = FindAnchor(source, field.AnchorText);
        if (hit is null)
        {
            // 找不到锚就跳过这个字段，而不是猜一个位置：一个盖错地方的签名
            // 比一个缺失的签名更难被发现。
            _logger.LogWarning(
                "Anchor text for field {FieldKey} was not found in the document; the field was left blank.",
                field.Key);
            return null;
        }

        // ★ 页码取**命中所在的页**，不是字段上记的那个：锚定位存在的全部理由就是
        //   上传文档的分页会随内容变动，此时字段记的页码正是那个不能信的东西。
        // Box 是面积最大的行框（标签折行时可能跨多行），用它作主定位框。
        var width = field.W > 0 ? (double)field.W : hit.Box.Width;
        var height = field.H > 0 ? (double)field.H : hit.Box.Height;
        return (hit.PageNumber, new NormalizedRect(hit.Box.X, hit.Box.Y, width, height));
    }

    /// <summary>锚文本在文档里的第一处命中（按页码、再按位置）；找不到为 null。</summary>
    private PdfTextMatch? FindAnchor(byte[] source, string anchorText)
        => _inspector.FindTags(source, AnchorPattern(anchorText))
            .OrderBy(m => m.PageNumber)
            .ThenBy(m => m.Box.Y)
            .ThenBy(m => m.Box.X)
            .FirstOrDefault();

    /// <summary>
    /// 锚文本 → 搜索用的正则：逐词按字面匹配（转义正则元字符），词与词之间允许任意空白，<b>也允许没有</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 锚是人写在文档里的一串字，不是给用户的正则接口，所以逐词转义。
    /// </para>
    /// <para>
    /// ★ 词间不能要求空格：<see cref="IPdfInspector.FindTags"/> 匹配的是<b>字母级拼接</b>出来的文本，排版空隙不在文本里：
    /// 很多 PDF 生成器用字距偏移而不是空格字形分词，一个折到下一行的词组两行之间也没有任何字符。
    /// 曾经把整段锚文本原样转义（空格成了字面的 <c>"\ "</c>），于是「Client Signature」这样的多词锚在这类文档里永远找不到，
    /// 密封时那个签名框被静默跳过，成品照样算哈希、出证书、归档。首尾空白同理不参与匹配。
    /// </para>
    /// </remarks>
    internal static string AnchorPattern(string anchorText)
    {
        Check.NotNullOrWhiteSpace(anchorText);
        var words = anchorText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(@"\s*", words.Select(Regex.Escape));
    }

    /// <summary>
    /// 一份要拿来密封的文档的预检：读得出页数、盖章器改得动它、每个锚文本都找得到。
    /// </summary>
    /// <param name="pdf">文档字节。</param>
    /// <param name="anchors">要找的锚：(字段键, 锚文本)。</param>
    /// <returns>
    /// 读不出或改不动时失败（400，说明原因）；否则给出真实页数与找不到锚的字段键。
    /// </returns>
    /// <remarks>
    /// ★ 这几件事若等到密封才发现，那时每一位签署人都已经签完了：页码越界、文档不让改都会让盖章抛异常，
    /// 请求退回 <c>InProgress</c>，而管理端重新密封是同一份文档、同一套落点，失败得一模一样，唯一的出路是作废、
    /// 让每个人重签一遍。找不到锚则更糟：不报错，那个字段被静默跳过。所以在建模板（与发起请求）的那一刻就用
    /// 密封时的同一个盖章器、同一套锚匹配试一遍。
    /// 「改不动」最常见的是带所有者密码（禁止编辑）的表格：读取方能打开它，盖章方以修改模式打开时拒绝。
    /// </remarks>
    public Result<SealableDocument> InspectForSealing(byte[] pdf, IEnumerable<(string Key, string AnchorText)> anchors)
    {
        Check.NotNull(pdf);
        Check.NotNull(anchors);

        PdfInfo info;
        try
        {
            info = _inspector.GetInfo(pdf);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "A document offered for signing could not be read as a PDF.");
            return Result<SealableDocument>.Failure("The document is not a readable PDF.", 400);
        }

        if (info.Pages.Count == 0)
            return Result<SealableDocument>.Failure("The document has no pages.", 400);

        try
        {
            // 什么都不画：只问盖章器能不能以修改模式打开并写回它（密封走的正是这一步）。
            _stamper.Stamp(pdf, new PdfStampRequest());
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "A document offered for signing cannot be modified by the stamper.");
            return Result<SealableDocument>.Failure(
                "The document cannot be modified (it may be password protected or restricted against editing), "
                + "so signatures could not be stamped onto it. Remove the restriction and upload it again.",
                400);
        }

        var unfound = new List<string>();
        foreach (var (key, anchorText) in anchors)
        {
            if (string.IsNullOrWhiteSpace(anchorText) || FindAnchor(pdf, anchorText) is null)
                unfound.Add(key);
        }

        return Result<SealableDocument>.Success(new SealableDocument(info.Pages.Count, unfound));
    }

    /// <summary>
    /// 在一份文档里找不到锚文本的锚定位字段（按快照顺序）。
    /// </summary>
    /// <remarks>与密封时的定位是同一套匹配（<see cref="AnchorPattern"/>）：这里说找得到，密封时就找得到。</remarks>
    public IReadOnlyList<SnapshotField> FindUnplacedAnchors(byte[] pdf, IEnumerable<SnapshotField> fields)
    {
        Check.NotNull(pdf);
        Check.NotNull(fields);

        return fields
            .Where(f => f.PlacementMode == FieldPlacementMode.Anchor)
            .Where(f => string.IsNullOrWhiteSpace(f.AnchorText) || FindAnchor(pdf, f.AnchorText) is null)
            .ToList();
    }

    /// <summary>
    /// 读一份请求名下的文档字节（调用方负责读取授予）；读不到为 null。
    /// </summary>
    public Task<byte[]?> ReadDocumentAsync(Guid fileId, CancellationToken cancellationToken = default)
        => ReadAsync(fileId, cancellationToken);

    /// <summary>
    /// 勾选框落的是字符，不是图，省掉一份要随主题走的资源。判定与提交时的必填校验同一个（<see cref="SigningFieldValues.IsChecked"/>）。
    /// </summary>
    private static string RenderCheckbox(string value)
        => SigningFieldValues.IsChecked(value) ? "X" : string.Empty;

    /// <summary>
    /// 密封成品的文件名。只由标题决定（请求的标题创建后不再变），所以事后补做归档或证书时算得出与密封那一刻相同的名字。
    /// </summary>
    internal static string SealedFileName(string title) => BuildFileName(title, "-signed");

    /// <summary>文件名：标题 + 后缀（<c>-draft</c> 渲染稿 / <c>-signed</c> 成品），非法字符换成下划线。</summary>
    private static string BuildFileName(string title, string suffix)
        => $"{SigningLimits.FileNameStem(title, 120)}{suffix}.pdf";
}
