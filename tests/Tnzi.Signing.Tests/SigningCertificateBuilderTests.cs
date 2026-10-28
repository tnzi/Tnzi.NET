using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tnzi.Documents.Models;
using Tnzi.Documents.Services;
using Tnzi.Results;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;
using Tnzi.Signing.Services.Internal;
using Tnzi.Storage.Entities;
using Tnzi.Storage.Services;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 完成证书的版式。
/// </summary>
/// <remarks>
/// 用记录调用的假 stamper（同 <see cref="ComposedDocumentRendererTests"/> 的理由）：这里问的是
/// **排版决策** —— 每一行落在第几页、有没有落在纸面之内 —— 不是 PDFsharp 画得对不对。
/// <para>
/// ★ 这组断言之所以重要：证书是整条签署证据链的落脚点。一行画到纸面之外不会报错、不会记日志，
/// 打开 PDF 也只是"下面没有了"，没有任何人会发现少了一位签署人。
/// </para>
/// </remarks>
public class SigningCertificateBuilderTests
{
    /// <summary>只记下收到的请求，返回一段可辨认的假字节。</summary>
    private sealed class RecordingStamper : IPdfStamper
    {
        public PdfStampRequest? LastCreate { get; private set; }

        public byte[] Stamp(byte[] pdf, PdfStampRequest request) => pdf;

        public byte[] Create(PdfStampRequest request)
        {
            LastCreate = request;
            return [0x25, 0x50, 0x44, 0x46];
        }

        public double MeasureTextWidth(string text, double fontSize, bool bold = false, bool italic = false)
            => TestTextMetrics.Width(text, fontSize, bold);
    }

    private static (SigningCertificateBuilder Builder, RecordingStamper Stamper) Create()
    {
        var stamper = new RecordingStamper();
        var files = new Mock<IFileStorageService>(MockBehavior.Loose);
        files.Setup(f => f.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(() => Result.Success(new FileRecord { Id = Guid.NewGuid() }));

        return (
            new SigningCertificateBuilder(stamper, files.Object, NullLogger<SigningCertificateBuilder>.Instance),
            stamper);
    }

    private static Envelope Envelope() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Settlement Agreement",
        Sha256 = new string('a', 64),
        CompletedAt = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc),
        SentByName = "Case Manager",
        Status = EnvelopeStatus.Completed,
    };

    private static Signer Signer(int order, bool withConsent = true) => new()
    {
        Id = Guid.NewGuid(),
        Order = order,
        Role = $"Party{order}",
        Name = $"Signer Number {order}",
        Email = $"signer{order}@example.com",
        Status = SigningRecipientStatus.Signed,
        ViewedAt = new DateTime(2026, 8, 9, 10, order, 0, DateTimeKind.Utc),
        SignedAt = new DateTime(2026, 8, 9, 11, order, 0, DateTimeKind.Utc),
        SignerIp = "203.0.113.7",
        SignerUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)",
        ConsentText = withConsent ? "I agree to sign this document electronically." : null,
    };

    private static List<Signer> Signers(int count, bool withConsent = true)
        => Enumerable.Range(1, count).Select(i => Signer(i, withConsent)).ToList();

    /// <summary>某个盖章是否整个落在它那一页的纸面之内。</summary>
    private static bool OnPaper(PdfStamp stamp)
        => stamp.Rect.Y >= 0d && stamp.Rect.Bottom <= 1d && stamp.Rect.X >= 0d && stamp.Rect.Right <= 1d;

    [Fact]
    public async Task Build_WithManySigners_DrawsNothingOffThePaper()
    {
        var (builder, stamper) = Create();

        var result = await builder.BuildAsync(Envelope(), Signers(8), "settlement-signed.pdf");

        result.Succeeded.ShouldBeTrue(result.Message);
        var request = stamper.LastCreate.ShouldNotBeNull();

        // ★ 一页装不下就必须翻页。落在 y > 1 的那些行不会报错也不会记日志 ——
        //   它们只是不见了，而不见的正是"谁在什么时候签的"。
        var offPaper = request.Stamps.Where(s => !OnPaper(s)).ToList();
        offPaper.ShouldBeEmpty(
            $"{offPaper.Count} stamp(s) were placed outside the page; " +
            $"the lowest sits at y={offPaper.Select(s => s.Rect.Bottom).DefaultIfEmpty(0d).Max():0.###}.");
    }

    [Fact]
    public async Task Build_WithManySigners_KeepsEverySignerOnTheCertificate()
    {
        var (builder, stamper) = Create();
        var signers = Signers(8);

        await builder.BuildAsync(Envelope(), signers, "settlement-signed.pdf");

        var request = stamper.LastCreate.ShouldNotBeNull();
        var visible = request.Stamps
            .OfType<PdfTextStamp>()
            .Where(OnPaper)
            .Select(s => s.Text)
            .ToList();

        // 每一位签署人都得在纸面上留下可读的一行 —— 少一位，这份证书就不能当证据用。
        foreach (var signer in signers)
            visible.ShouldContain(t => t.Contains(signer.Name, StringComparison.Ordinal), $"{signer.Name} is missing.");
    }

    [Fact]
    public async Task Build_PageNumbers_NeverExceedTheAppendedPages()
    {
        var (builder, stamper) = Create();

        await builder.BuildAsync(Envelope(), Signers(12), "settlement-signed.pdf");

        var request = stamper.LastCreate.ShouldNotBeNull();
        request.AppendPages.Count.ShouldBeGreaterThan(0);
        foreach (var stamp in request.Stamps)
        {
            // 页码越界会让 PdfSharpPdfStamper 抛 ArgumentOutOfRangeException（整份证书生成失败），
            // 这条断言把"翻页"与"造页"钉在一起。
            stamp.PageNumber.ShouldBeInRange(1, request.AppendPages.Count);
        }
    }

    [Fact]
    public async Task Build_WithFewSigners_StillFitsOnASinglePage()
    {
        var (builder, stamper) = Create();

        await builder.BuildAsync(Envelope(), Signers(2), "settlement-signed.pdf");

        // 常见情形（两方合同）不该因为支持翻页而白白多出一页纸。
        stamper.LastCreate.ShouldNotBeNull().AppendPages.Count.ShouldBe(1);
    }

    /// <remarks>
    /// 长同意声明那一组钉住的是「整段高度按换行之后的行数算」：只按行条目数算的话，一段五六行的声明
    /// 会让整段比预留的高，末尾几行溢到下一页。
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Build_DoesNotSplitOneSignerAcrossTwoPages(bool longConsent)
    {
        var (builder, stamper) = Create();
        var signers = Signers(8);
        if (longConsent)
        {
            foreach (var signer in signers)
                signer.ConsentText = LongConsent;
        }

        await builder.BuildAsync(Envelope(), signers, "settlement-signed.pdf");

        var request = stamper.LastCreate.ShouldNotBeNull();
        var stamps = request.Stamps.OfType<PdfTextStamp>().ToList();

        // 姓名行的位置就是每一段的起点；一段读到下一个姓名行为止。
        var starts = Enumerable.Range(0, stamps.Count)
            .Where(i => stamps[i].Text.Contains("Signer Number", StringComparison.Ordinal))
            .ToList();
        starts.Count.ShouldBe(8);

        // 一位签署人的姓名行与它下面的明细必须在同一页：跨页断开的记录读起来像是
        // 两个人各签了一半。
        for (var i = 0; i < starts.Count; i++)
        {
            var from = starts[i];
            // 末段读到脚注为止 —— 脚注是独立一行，允许它自己翻页。
            var to = i + 1 < starts.Count
                ? starts[i + 1]
                : stamps.FindIndex(from, s => s.Text.StartsWith("This certificate", StringComparison.Ordinal)) is var note && note >= 0
                    ? note
                    : stamps.Count;
            var page = stamps[from].PageNumber;
            foreach (var line in stamps.Skip(from).Take(to - from))
                line.PageNumber.ShouldBe(page, $"{stamps[from].Text} was split across pages.");
        }
    }

    // 证书版式：A4，左右边距各 8%，标签列 22%。值列与姓名行各自的宽度（pt）。
    private const double RowValueWidthPoints = 595d * (1d - (0.08d * 2) - 0.22d);
    private const double ContentWidthPoints = 595d * (1d - (0.08d * 2));

    /// <summary>
    /// 一个按字符数「放得下」的全大写标题，量出来越过了值列：按实测宽度换行，每一行都在值列之内，而且一个字都不丢。
    /// </summary>
    [Fact]
    public async Task Build_AllCapsTitle_WrapsByMeasuredWidth_WithoutLosingText()
    {
        var (builder, stamper) = Create();
        var envelope = Envelope();
        envelope.Title = "MASTER SERVICES AGREEMENT AND STATEMENT OF WORK FOR WAREHOUSE OPERATIONS";

        (await builder.BuildAsync(envelope, Signers(1), "msa-signed.pdf")).Succeeded.ShouldBeTrue();

        var lines = ValueLinesOfRow(stamper, "Document");
        lines.Count.ShouldBeGreaterThan(1);
        foreach (var line in lines)
            TestTextMetrics.Width(line.Text, line.FontSize, line.Bold).ShouldBeLessThanOrEqualTo(RowValueWidthPoints);
        string.Join(" ", lines.Select(l => l.Text)).ShouldBe(envelope.Title);
    }

    /// <summary>放得下的值原样画成一行，不平白换行（SHA-256 那一行尤其要完整地在一行里）。</summary>
    [Fact]
    public async Task Build_ValuesThatFit_AreDrawnUnchanged()
    {
        var (builder, stamper) = Create();
        var envelope = Envelope();

        await builder.BuildAsync(envelope, Signers(1), "settlement-signed.pdf");

        ValueLinesOfRow(stamper, "SHA-256").Select(l => l.Text).ShouldBe([envelope.Sha256!]);
        ValueLinesOfRow(stamper, "Document").Select(l => l.Text).ShouldBe([envelope.Title]);
    }

    /// <summary>姓名 + 邮箱的粗体行同样按实测宽度换行：每一行都在正文宽度之内，邮箱一个字都不丢。</summary>
    [Fact]
    public async Task Build_LongSignerNameAndEmail_WrapInsideTheContentWidth()
    {
        var (builder, stamper) = Create();
        var signer = Signer(1);
        signer.Name = "MAXIMILIAN ALEXANDER WOLFGANG MONTGOMERY-WORTHINGTON";
        signer.Email = "maximilian.alexander.montgomery-worthington@wholesale-warehouse-operations.example.com";

        await builder.BuildAsync(Envelope(), [signer], "settlement-signed.pdf");

        var nameLines = stamper.LastCreate.ShouldNotBeNull().Stamps.OfType<PdfTextStamp>()
            .Where(s => s.Bold && s.FontSize == 10d)
            .ToList();
        nameLines.Count.ShouldBeGreaterThan(1);
        foreach (var line in nameLines)
            TestTextMetrics.Width(line.Text, line.FontSize, line.Bold).ShouldBeLessThanOrEqualTo(ContentWidthPoints);
        nameLines[0].Text.ShouldStartWith("1. MAXIMILIAN");
        string.Concat(nameLines.Select(l => l.Text)).ShouldContain(signer.Email);
        nameLines.ShouldAllBe(l => !l.Text.Contains('…'));
    }

    /// <summary>
    /// ★ 同意声明是证据：证书要能回答「他当时同意的是什么」。一段常见的几百字电子签名同意声明，
    /// 证书上必须是全文（分成若干行），不能只剩开头一行加省略号。
    /// </summary>
    [Fact]
    public async Task Build_LongConsent_IsPrintedInFull_AcrossLines()
    {
        var (builder, stamper) = Create();
        var signer = Signer(1);
        signer.ConsentText = LongConsent;

        (await builder.BuildAsync(Envelope(), [signer], "settlement-signed.pdf")).Succeeded.ShouldBeTrue();

        var lines = ValueLinesOfRow(stamper, "    Consent");
        lines.Count.ShouldBeGreaterThan(3);
        foreach (var line in lines)
            TestTextMetrics.Width(line.Text, line.FontSize, line.Bold).ShouldBeLessThanOrEqualTo(RowValueWidthPoints);
        string.Join(" ", lines.Select(l => l.Text)).ShouldBe(LongConsent);
    }

    /// <summary>User-Agent 与拒签理由同理：前者回答「在哪台机器上按的」，后者是当事人的原话。</summary>
    [Fact]
    public async Task Build_LongUserAgentAndDeclineReason_ArePrintedInFull()
    {
        var (builder, stamper) = Create();
        var signer = Signer(1);
        signer.Status = SigningRecipientStatus.Declined;
        signer.SignerUserAgent =
            "Mozilla/5.0 (Linux; Android 14; SM-S918B Build/UP1A.231005.007; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/129.0.6668.81 Mobile Safari/537.36";
        signer.DeclinedAt = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        signer.DeclineReason = string.Join(" ", Enumerable.Repeat("The indemnity clause in section seven does not match what we agreed on the call.", 12));

        (await builder.BuildAsync(Envelope(), [signer], "settlement-signed.pdf")).Succeeded.ShouldBeTrue();

        string.Join(" ", ValueLinesOfRow(stamper, "    User agent").Select(l => l.Text)).ShouldBe(signer.SignerUserAgent);
        string.Join(" ", ValueLinesOfRow(stamper, "    Reason").Select(l => l.Text)).ShouldBe(signer.DeclineReason);
    }

    /// <summary>
    /// 一段比一页还长的同意声明（上限 4000 字）跨页续写：一个字都不丢，没有一行落在纸面之外，
    /// 页码不超过造出来的页数，后面那位签署人也还在。
    /// </summary>
    [Fact]
    public async Task Build_ConsentLongerThanAPage_ContinuesOnTheNextPage()
    {
        var (builder, stamper) = Create();
        var signers = Signers(2);
        var huge = string.Join(" ", Enumerable.Repeat(LongConsent, 9));
        huge = huge[..Math.Min(huge.Length, SigningLimits.MaxConsentTextLength)].TrimEnd();
        signers[0].ConsentText = huge;

        (await builder.BuildAsync(Envelope(), signers, "settlement-signed.pdf")).Succeeded.ShouldBeTrue();

        var request = stamper.LastCreate.ShouldNotBeNull();
        request.AppendPages.Count.ShouldBeGreaterThan(1);
        request.Stamps.Where(s => !OnPaper(s)).ShouldBeEmpty();
        foreach (var stamp in request.Stamps)
            stamp.PageNumber.ShouldBeInRange(1, request.AppendPages.Count);

        string.Join(" ", ValueLinesOfRow(stamper, "    Consent").Select(l => l.Text)).ShouldBe(huge);
        request.Stamps.OfType<PdfTextStamp>().ShouldContain(s => s.Text.Contains(signers[1].Name, StringComparison.Ordinal));
    }

    /// <summary>
    /// 一位签署人的同意声明换成二三十行：整段放不进第一页剩下的地方，就整段挪到下一页，而不是从第一页底部开始、
    /// 后半截续到第二页。整段高度必须按换行之后的行数算，按行条目数算会以为放得下。
    /// </summary>
    [Fact]
    public async Task Build_ATallSignerBlock_MovesToTheNextPageWhole()
    {
        var (builder, stamper) = Create();
        var signers = Signers(2);
        signers[1].ConsentText = string.Join(" ", Enumerable.Repeat(LongConsent, 5));

        (await builder.BuildAsync(Envelope(), signers, "settlement-signed.pdf")).Succeeded.ShouldBeTrue();

        var stamps = stamper.LastCreate.ShouldNotBeNull().Stamps.OfType<PdfTextStamp>().ToList();
        var start = stamps.FindIndex(s => s.Text.Contains(signers[1].Name, StringComparison.Ordinal));
        var end = stamps.FindIndex(start, s => s.Text.StartsWith("This certificate", StringComparison.Ordinal));
        start.ShouldBeGreaterThanOrEqualTo(0);
        end.ShouldBeGreaterThan(start);
        var block = stamps.Skip(start).Take(end - start).ToList();
        block.Select(s => s.PageNumber).Distinct().Count().ShouldBe(1, "the second signer's record was split across pages");
        block[0].PageNumber.ShouldBe(2);
    }

    /// <summary>一段约 400 字、按常见电子签名同意声明写的条款。</summary>
    private const string LongConsent =
        "I agree that my electronic signature is the legal equivalent of my manual signature on this document. "
        + "I consent to be legally bound by its terms and conditions, and I agree to conduct this transaction by electronic means. "
        + "I understand that I may request a paper copy at any time, that I may withdraw this consent before signing, "
        + "and that withdrawing it does not affect anything I signed before the withdrawal took effect.";

    /// <summary>
    /// 量字失败（没有字体）只让证书缺席，不能把异常抛给已经密封完的调用方：
    /// 为一页审计记录把一份有效的签署结果撤回去是本末倒置。
    /// </summary>
    [Fact]
    public async Task Build_WhenMeasuringFails_ReturnsAFailureInsteadOfThrowing()
    {
        var stamper = new Mock<IPdfStamper>();
        stamper.Setup(s => s.MeasureTextWidth(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Throws(new Tnzi.Documents.Exceptions.PdfDocumentException("No usable system font was found."));
        var builder = new SigningCertificateBuilder(stamper.Object, new Mock<IFileStorageService>().Object, NullLogger<SigningCertificateBuilder>.Instance);

        var result = await builder.BuildAsync(Envelope(), Signers(1), "settlement-signed.pdf");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(500);
    }

    /// <summary>
    /// 某一行（按标签认）值那一栏的全部行：标签之后、落在值列里的连续几格，直到下一个不在值列里的盖章（下一个标签或姓名行）。
    /// </summary>
    private static List<PdfTextStamp> ValueLinesOfRow(RecordingStamper stamper, string label)
    {
        const double valueColumnX = 0.08d + 0.22d;
        var stamps = stamper.LastCreate.ShouldNotBeNull().Stamps.OfType<PdfTextStamp>().ToList();
        var labelIndex = stamps.FindIndex(s => s.Text == label);
        labelIndex.ShouldBeGreaterThanOrEqualTo(0, $"No row labelled '{label}'.");
        return stamps
            .Skip(labelIndex + 1)
            .TakeWhile(s => Math.Abs(s.Rect.X - valueColumnX) < 1e-9)
            .ToList();
    }
}
