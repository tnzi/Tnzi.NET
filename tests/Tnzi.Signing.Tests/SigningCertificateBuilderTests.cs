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

    [Fact]
    public async Task Build_DoesNotSplitOneSignerAcrossTwoPages()
    {
        var (builder, stamper) = Create();

        await builder.BuildAsync(Envelope(), Signers(8), "settlement-signed.pdf");

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
}
