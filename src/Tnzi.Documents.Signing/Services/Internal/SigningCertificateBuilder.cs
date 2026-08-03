namespace Tnzi.Documents.Signing.Services.Internal;

/// <summary>
/// 生成完成证书：一份**独立**的 PDF，记录这次签署到底发生了什么。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么是独立文件，而不是追加到成品末尾。</b>两个理由，第二个是决定性的：
/// ① 成品必须原样就是当初签的那一份 —— 一份要拿去存档或登记的合同，末尾多一页审计
///    记录常常是不能接受的；
/// ② 证书要写上<b>成品的 SHA-256</b>，而追加会改变成品的字节，于是哈希写不进去 ——
///    先有鸡还是先有蛋。分开之后，证书指向成品的哈希，这条指向才是「这份 PDF 就是
///    当初签的那份」的全部证据价值所在。
/// </para>
/// <para>
/// 版式刻意保持朴素：一列标签 + 值。它是证据不是宣传页，而且每多一分版式，就多一分
/// 在没有中文字体的精简容器里画不出来的可能。
/// </para>
/// </remarks>
public sealed class SigningCertificateBuilder
{
    // A4 纵向（pt）。刻意固定而不是跟随成品尺寸：证书不必与被签文档同开本，
    // 固定尺寸让版式常数只需要调一次。
    private const double PageWidthPoints = 595d;
    private const double PageHeightPoints = 842d;

    // 归一化版式常数（0-1，左上角原点，与 Tnzi.Documents 同一口径）
    private const double MarginX = 0.08d;
    private const double ContentWidth = 1d - (MarginX * 2);
    private const double LineHeight = 0.0175d;
    private const double SectionGap = 0.012d;

    private readonly IPdfStamper _stamper;
    private readonly IFileStorageService _files;
    private readonly ILogger<SigningCertificateBuilder> _logger;

    public SigningCertificateBuilder(
        IPdfStamper stamper,
        IFileStorageService files,
        ILogger<SigningCertificateBuilder> logger)
    {
        _stamper = Check.NotNull(stamper);
        _files = Check.NotNull(files);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 生成并存下完成证书。
    /// </summary>
    /// <param name="request">已密封的请求（<see cref="Envelope.Sha256"/> 必须已经算出）。</param>
    /// <param name="recipients">全部收件人（按签署顺序）。</param>
    /// <param name="sealedFileName">成品文件名，写进证书好让两份文件能对上。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<Result<Guid>> BuildAsync(
        Envelope request,
        IReadOnlyList<Signer> recipients,
        string sealedFileName,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);
        Check.NotNull(recipients);

        var stamps = new List<PdfStamp>();
        var y = 0.07d;

        y = WriteHeading(stamps, "Certificate of Completion", y);
        y += SectionGap;

        y = WriteRow(stamps, "Document", request.Title, y);
        y = WriteRow(stamps, "File", sealedFileName, y);
        y = WriteRow(stamps, "Request ID", request.Id.ToString(), y);
        // ★ 这一行是整份证书的重点：它把证书与某一份确定的字节绑在一起。
        y = WriteRow(stamps, "SHA-256", request.Sha256 ?? "(not computed)", y);
        y = WriteRow(stamps, "Completed (UTC)", Format(request.CompletedAt), y);
        if (!string.IsNullOrWhiteSpace(request.SentByName))
            y = WriteRow(stamps, "Sent by", request.SentByName!, y);

        y += SectionGap;
        y = WriteHeading(stamps, "Signers", y, fontSize: 12d);
        y += SectionGap / 2;

        foreach (var recipient in recipients.OrderBy(r => r.Order).ThenBy(r => r.Name, StringComparer.Ordinal))
            y = WriteRecipient(stamps, recipient, y);

        y += SectionGap;
        WriteNote(stamps, y);

        byte[] pdf;
        try
        {
            pdf = _stamper.Create(new PdfStampRequest
            {
                AppendPages = [new PdfPageSpec { WidthPoints = PageWidthPoints, HeightPoints = PageHeightPoints }],
                Stamps = stamps,
            });
        }
        catch (Exception ex)
        {
            // ★ 证书生成失败**不能**让已经密封的请求回退。文档已经签成、哈希已经算定，
            //   为一页审计记录把一份有效的签署结果撤回去是本末倒置。调用方据此只记日志。
            _logger.LogError(ex, "Building the completion certificate for request {RequestId} failed.", request.Id);
            return Result<Guid>.Failure("The completion certificate could not be generated.", 500);
        }

        using var output = new MemoryStream(pdf, writable: false);
        var saved = await _files.SaveAsync(BuildFileName(request.Title), output);
        if (!saved.Succeeded || saved.Data is null)
            return Result<Guid>.Failure("The completion certificate could not be stored.", 500);

        return Result<Guid>.Success(saved.Data.Id);
    }

    private static double WriteHeading(List<PdfStamp> stamps, string text, double y, double fontSize = 16d)
    {
        stamps.Add(new PdfTextStamp
        {
            PageNumber = 1,
            Rect = new NormalizedRect(MarginX, y, ContentWidth, LineHeight * 1.6),
            Text = text,
            FontSize = fontSize,
            Bold = true,
            Alignment = PdfStampAlignment.CenterLeft,
        });
        return y + (LineHeight * 1.6);
    }

    private static double WriteRow(List<PdfStamp> stamps, string label, string value, double y)
    {
        const double labelWidth = 0.22d;
        stamps.Add(new PdfTextStamp
        {
            PageNumber = 1,
            Rect = new NormalizedRect(MarginX, y, labelWidth, LineHeight),
            Text = label,
            FontSize = 9d,
            Color = "#666666",
            Alignment = PdfStampAlignment.CenterLeft,
        });
        stamps.Add(new PdfTextStamp
        {
            PageNumber = 1,
            Rect = new NormalizedRect(MarginX + labelWidth, y, ContentWidth - labelWidth, LineHeight),
            // 长值（哈希、UA 串）不换行会被裁掉右半 —— 裁掉的哈希等于没有哈希，
            // 所以这里按框宽截断并显式加省略号，让"这是被截断的"看得出来。
            Text = Truncate(value, 74),
            FontSize = 9d,
            Alignment = PdfStampAlignment.CenterLeft,
        });
        return y + LineHeight;
    }

    private double WriteRecipient(List<PdfStamp> stamps, Signer recipient, double y)
    {
        var who = string.IsNullOrWhiteSpace(recipient.Email)
            ? recipient.Name
            : $"{recipient.Name} <{recipient.Email}>";

        stamps.Add(new PdfTextStamp
        {
            PageNumber = 1,
            Rect = new NormalizedRect(MarginX, y, ContentWidth, LineHeight),
            Text = $"{recipient.Order}. {who}",
            FontSize = 10d,
            Bold = true,
            Alignment = PdfStampAlignment.CenterLeft,
        });
        y += LineHeight;

        y = WriteRow(stamps, "    Role", string.IsNullOrWhiteSpace(recipient.Role) ? "-" : recipient.Role, y);
        y = WriteRow(stamps, "    Status", recipient.Status.ToString(), y);
        y = WriteRow(stamps, "    Viewed (UTC)", Format(recipient.ViewedAt), y);
        y = WriteRow(stamps, "    Signed (UTC)", Format(recipient.SignedAt), y);
        if (recipient.DeclinedAt.HasValue)
        {
            y = WriteRow(stamps, "    Declined (UTC)", Format(recipient.DeclinedAt), y);
            y = WriteRow(stamps, "    Reason", recipient.DeclineReason ?? "-", y);
        }
        // IP 与 UA 是这份证书里唯一能回答"是不是本人在那台机器上按的"的两行；
        // 缺失时写 "-" 而不是省略这一行 —— 一行不存在与一行为空是两回事。
        y = WriteRow(stamps, "    IP", recipient.SignerIp ?? "-", y);
        y = WriteRow(stamps, "    User agent", recipient.SignerUserAgent ?? "-", y);
        if (!string.IsNullOrWhiteSpace(recipient.ConsentText))
            y = WriteRow(stamps, "    Consent", recipient.ConsentText!, y);

        return y + (SectionGap / 2);
    }

    private static void WriteNote(List<PdfStamp> stamps, double y)
    {
        stamps.Add(new PdfTextStamp
        {
            PageNumber = 1,
            Rect = new NormalizedRect(MarginX, y, ContentWidth, LineHeight),
            Text = "This certificate accompanies the signed document identified by the SHA-256 above.",
            FontSize = 8d,
            Italic = true,
            Color = "#666666",
            Alignment = PdfStampAlignment.CenterLeft,
        });
    }

    private static string Format(DateTime? value)
        => value?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "-";

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..(max - 1)] + "…";

    private static string BuildFileName(string title)
    {
        var safe = string.IsNullOrWhiteSpace(title) ? "document" : title.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '_');
        if (safe.Length > 110) safe = safe[..110];
        return $"{safe}-certificate.pdf";
    }
}
