using System.Security.Cryptography;

namespace Tnzi.Signing.Services.Internal;

/// <summary>密封结果：成品文件 id 与它的哈希锚点。</summary>
public sealed record SealResult(Guid FileId, string Sha256, string FileName);

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

        var pdfResult = await _files.GetAsync(renderedId);
        if (!pdfResult.Succeeded || pdfResult.Data is null)
            return Result<SealResult>.Failure("The rendered document could not be read.", 404);

        byte[] source;
        await using (var stream = pdfResult.Data)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            source = buffer.ToArray();
        }

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
        var fileName = BuildFileName(request.Title);

        using var output = new MemoryStream(sealed_, writable: false);
        var saved = await _files.SaveAsync(fileName, output);
        if (!saved.Succeeded || saved.Data is null)
            return Result<SealResult>.Failure("The sealed document could not be stored.", 500);

        return Result<SealResult>.Success(new SealResult(saved.Data.Id, hash, fileName));
    }

    /// <summary>把每个字段的取值翻译成一次盖章。</summary>
    private List<PdfStamp> BuildStamps(
        byte[] source,
        SigningSnapshot snapshot,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyList<Signer> recipients)
    {
        var stamps = new List<PdfStamp>();

        // 签名图按角色取：字段声明的是"谁来签"，落笔的是那个人交上来的图。
        var signatureByRole = recipients
            .Where(r => !string.IsNullOrEmpty(r.SignatureImage) && !string.IsNullOrWhiteSpace(r.Role))
            .GroupBy(r => r.Role, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().SignatureImage!, StringComparer.OrdinalIgnoreCase);

        foreach (var field in snapshot.Fields)
        {
            if (ResolvePlacement(source, field) is not { } placement) continue;
            var (page, target) = placement;

            if (field.IsSignatureLike)
            {
                if (field.RecipientRole is { } role && signatureByRole.TryGetValue(role, out var image))
                {
                    stamps.Add(new PdfImageStamp
                    {
                        PageNumber = page,
                        Rect = target,
                        DataUrl = image,
                        PreserveAspectRatio = true,
                    });
                }
                continue;
            }

            if (!values.TryGetValue(field.Key, out var value) || string.IsNullOrEmpty(value))
                continue;

            stamps.Add(new PdfTextStamp
            {
                PageNumber = page,
                Rect = target,
                Text = field.Type == SigningFieldType.Checkbox ? RenderCheckbox(value) : value,
                FontSize = (double)(field.FontSize ?? 10m),
                Alignment = PdfStampAlignment.CenterLeft,
            });
        }

        return stamps;
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

        // 锚文本按字面匹配（转义正则元字符）：锚是人写在文档里的一串字，
        // 不是给用户的正则接口。
        var matches = _inspector.FindTags(source, Regex.Escape(field.AnchorText));
        var hit = matches.FirstOrDefault();
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

    /// <summary>勾选框落的是字符，不是图 —— 省掉一份要随主题走的资源。</summary>
    private static string RenderCheckbox(string value)
        => value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" ? "X" : string.Empty;

    /// <summary>成品文件名：标题 + 后缀，非法字符换成下划线。</summary>
    private static string BuildFileName(string title)
    {
        var safe = string.IsNullOrWhiteSpace(title) ? "document" : title.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '_');

        // 文件名长度在各文件系统上限不同，留出后缀余量。
        if (safe.Length > 120) safe = safe[..120];
        return $"{safe}-signed.pdf";
    }
}
