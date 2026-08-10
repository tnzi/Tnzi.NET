namespace Tnzi.Signing.Services.Internal;

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
/// <para>
/// ★ <b>装不下就翻页，页数由内容定。</b>曾经是固定一页：签署人一多，后面几位就被画到
/// 纸面之外 —— 不报错、不记日志，打开 PDF 也只是"下面没有了"。一份少了两位当事人的
/// 完成证书<b>不能当证据用</b>，而多方合同（几个被告 + 几家保险公司）很容易到那个量。
/// 翻页逻辑与同目录的 <see cref="ComposedDocumentRenderer"/> 同一套 <c>EnsureRoom</c> 写法。
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

    /// <summary>正文区的上下边界：越过下界就翻页。</summary>
    private const double TopY = 0.07d;
    private const double BottomY = 0.94d;

    /// <summary>排版游标：写到第几页的哪个高度。翻页只会让 <paramref name="Page"/> 变大。</summary>
    private readonly record struct Cursor(int Page, double Y);

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
        var cursor = new Cursor(1, TopY);

        cursor = WriteHeading(stamps, "Certificate of Completion", cursor);
        cursor = Advance(cursor, SectionGap);

        cursor = WriteRow(stamps, "Document", request.Title, cursor);
        cursor = WriteRow(stamps, "File", sealedFileName, cursor);
        cursor = WriteRow(stamps, "Request ID", request.Id.ToString(), cursor);
        // ★ 这一行是整份证书的重点：它把证书与某一份确定的字节绑在一起。
        cursor = WriteRow(stamps, "SHA-256", request.Sha256 ?? "(not computed)", cursor);
        cursor = WriteRow(stamps, "Completed (UTC)", Format(request.CompletedAt), cursor);
        if (!string.IsNullOrWhiteSpace(request.SentByName))
            cursor = WriteRow(stamps, "Sent by", request.SentByName!, cursor);

        cursor = Advance(cursor, SectionGap);
        cursor = WriteHeading(stamps, "Signers", cursor, fontSize: 12d);
        cursor = Advance(cursor, SectionGap / 2);

        foreach (var recipient in recipients.OrderBy(r => r.Order).ThenBy(r => r.Name, StringComparer.Ordinal))
            cursor = WriteRecipient(stamps, recipient, cursor);

        cursor = Advance(cursor, SectionGap);
        cursor = WriteNote(stamps, cursor);

        byte[] pdf;
        try
        {
            pdf = _stamper.Create(new PdfStampRequest
            {
                // 页数由排版结果决定 —— 游标只会往后翻，所以末值就是总页数。
                // 少造一页 = PdfSharpPdfStamper 直接抛页码越界，整份证书生成不出来。
                AppendPages = Enumerable.Range(0, cursor.Page)
                    .Select(_ => new PdfPageSpec { WidthPoints = PageWidthPoints, HeightPoints = PageHeightPoints })
                    .ToList(),
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

    /// <summary>纯挪动游标（段间留白），不画东西。越界与否交给下一次 <see cref="EnsureRoom"/> 判。</summary>
    private static Cursor Advance(Cursor cursor, double delta) => cursor with { Y = cursor.Y + delta };

    /// <summary>放不下就翻页。返回翻页之后的游标。</summary>
    private static Cursor EnsureRoom(Cursor cursor, double needed)
        => cursor.Y + needed > BottomY ? new Cursor(cursor.Page + 1, TopY) : cursor;

    private static Cursor WriteHeading(List<PdfStamp> stamps, string text, Cursor cursor, double fontSize = 16d)
    {
        const double height = LineHeight * 1.6;
        cursor = EnsureRoom(cursor, height);

        stamps.Add(new PdfTextStamp
        {
            PageNumber = cursor.Page,
            Rect = new NormalizedRect(MarginX, cursor.Y, ContentWidth, height),
            Text = text,
            FontSize = fontSize,
            Bold = true,
            Alignment = PdfStampAlignment.CenterLeft,
        });
        return Advance(cursor, height);
    }

    private static Cursor WriteRow(List<PdfStamp> stamps, string label, string value, Cursor cursor)
    {
        const double labelWidth = 0.22d;
        cursor = EnsureRoom(cursor, LineHeight);

        stamps.Add(new PdfTextStamp
        {
            PageNumber = cursor.Page,
            Rect = new NormalizedRect(MarginX, cursor.Y, labelWidth, LineHeight),
            Text = label,
            FontSize = 9d,
            Color = "#666666",
            Alignment = PdfStampAlignment.CenterLeft,
        });
        stamps.Add(new PdfTextStamp
        {
            PageNumber = cursor.Page,
            Rect = new NormalizedRect(MarginX + labelWidth, cursor.Y, ContentWidth - labelWidth, LineHeight),
            // 长值（哈希、UA 串）不换行会被裁掉右半 —— 裁掉的哈希等于没有哈希，
            // 所以这里按框宽截断并显式加省略号，让"这是被截断的"看得出来。
            Text = Truncate(value, 74),
            FontSize = 9d,
            Alignment = PdfStampAlignment.CenterLeft,
        });
        return Advance(cursor, LineHeight);
    }

    private Cursor WriteRecipient(List<PdfStamp> stamps, Signer recipient, Cursor cursor)
    {
        // ★ 一位签署人的记录整段不拆页：姓名在上一页、签署时间在下一页，读起来像是
        //   两个人各签了一半。整段比一页还高时不能空转（会翻出一堆空白页），
        //   那时退回逐行翻页 —— 行级 EnsureRoom 始终兜底。
        var block = BlockHeight(recipient);
        if (block <= BottomY - TopY)
            cursor = EnsureRoom(cursor, block);

        var who = string.IsNullOrWhiteSpace(recipient.Email)
            ? recipient.Name
            : $"{recipient.Name} <{recipient.Email}>";

        cursor = EnsureRoom(cursor, LineHeight);
        stamps.Add(new PdfTextStamp
        {
            PageNumber = cursor.Page,
            Rect = new NormalizedRect(MarginX, cursor.Y, ContentWidth, LineHeight),
            Text = $"{recipient.Order}. {who}",
            FontSize = 10d,
            Bold = true,
            Alignment = PdfStampAlignment.CenterLeft,
        });
        cursor = Advance(cursor, LineHeight);

        cursor = WriteRow(stamps, "    Role", string.IsNullOrWhiteSpace(recipient.Role) ? "-" : recipient.Role, cursor);
        cursor = WriteRow(stamps, "    Status", recipient.Status.ToString(), cursor);
        cursor = WriteRow(stamps, "    Viewed (UTC)", Format(recipient.ViewedAt), cursor);
        cursor = WriteRow(stamps, "    Signed (UTC)", Format(recipient.SignedAt), cursor);
        if (recipient.DeclinedAt.HasValue)
        {
            cursor = WriteRow(stamps, "    Declined (UTC)", Format(recipient.DeclinedAt), cursor);
            cursor = WriteRow(stamps, "    Reason", recipient.DeclineReason ?? "-", cursor);
        }
        // IP 与 UA 是这份证书里唯一能回答"是不是本人在那台机器上按的"的两行；
        // 缺失时写 "-" 而不是省略这一行 —— 一行不存在与一行为空是两回事。
        cursor = WriteRow(stamps, "    IP", recipient.SignerIp ?? "-", cursor);
        cursor = WriteRow(stamps, "    User agent", recipient.SignerUserAgent ?? "-", cursor);
        if (!string.IsNullOrWhiteSpace(recipient.ConsentText))
            cursor = WriteRow(stamps, "    Consent", recipient.ConsentText!, cursor);

        return Advance(cursor, SectionGap / 2);
    }

    /// <summary>一位签署人整段占多高（用来判断要不要提前翻页）。必须与 <see cref="WriteRecipient"/> 写的行数一致。</summary>
    private static double BlockHeight(Signer recipient)
    {
        var rows = 1 // 姓名行
                   + 4 // Role / Status / Viewed / Signed
                   + 2 // IP / User agent
                   + (recipient.DeclinedAt.HasValue ? 2 : 0)
                   + (string.IsNullOrWhiteSpace(recipient.ConsentText) ? 0 : 1);
        return (rows * LineHeight) + (SectionGap / 2);
    }

    private static Cursor WriteNote(List<PdfStamp> stamps, Cursor cursor)
    {
        cursor = EnsureRoom(cursor, LineHeight);
        stamps.Add(new PdfTextStamp
        {
            PageNumber = cursor.Page,
            Rect = new NormalizedRect(MarginX, cursor.Y, ContentWidth, LineHeight),
            Text = "This certificate accompanies the signed document identified by the SHA-256 above.",
            FontSize = 8d,
            Italic = true,
            Color = "#666666",
            Alignment = PdfStampAlignment.CenterLeft,
        });
        return Advance(cursor, LineHeight);
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
