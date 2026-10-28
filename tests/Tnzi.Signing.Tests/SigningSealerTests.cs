using Microsoft.Extensions.Logging;
using Moq;
using Tnzi.Documents.Models;
using Tnzi.Documents.Services;
using Tnzi.Geometry;
using Tnzi.Results;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;
using Tnzi.Signing.Services.Internal;
using Tnzi.Storage.Entities;
using Tnzi.Storage.Services;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 密封器把快照翻译成盖章的那一步。
/// </summary>
/// <remarks>
/// 用记录调用的假 stamper：这里问的是「哪些字段被盖、哪些被跳过、跳过时有没有说话」，
/// 不是 PDFsharp 画得对不对。
/// </remarks>
public class SigningSealerTests
{
    private const string TinyPng =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private sealed class RecordingStamper : IPdfStamper
    {
        public PdfStampRequest? LastStamp { get; private set; }

        public byte[] Stamp(byte[] pdf, PdfStampRequest request)
        {
            LastStamp = request;
            return [.. pdf, 0x01];
        }

        public byte[] Create(PdfStampRequest request) => [0x25, 0x50, 0x44, 0x46];

        public double MeasureTextWidth(string text, double fontSize, bool bold = false, bool italic = false)
            => TestTextMetrics.Width(text, fontSize, bold);
    }

    /// <summary>把每条日志的渲染文本记下来。</summary>
    private sealed class ListLogger : ILogger<SigningSealer>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static (SigningSealer Sealer, RecordingStamper Stamper, ListLogger Logger) Create()
    {
        var stamper = new RecordingStamper();
        var logger = new ListLogger();
        var files = new Mock<IFileStorageService>(MockBehavior.Loose);
        files.Setup(f => f.GetAsync(It.IsAny<Guid>()))
            .ReturnsAsync(() => Result.Success<Stream>(new MemoryStream([0x25, 0x50, 0x44, 0x46])));
        files.Setup(f => f.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(() => Result.Success(new FileRecord { Id = Guid.NewGuid() }));

        return (new SigningSealer(stamper, new Mock<IPdfInspector>().Object, files.Object, logger), stamper, logger);
    }

    private static SnapshotField SignatureField(string? role) => new()
    {
        Key = "sig",
        Label = "Signature",
        Type = SigningFieldType.Signature,
        RecipientRole = role,
        PlacementMode = FieldPlacementMode.Absolute,
        Page = 1,
        X = 0.1m,
        Y = 0.8m,
        W = 0.3m,
        H = 0.05m,
    };

    private static Envelope Envelope() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Engagement Letter",
        RenderedPdfFileId = Guid.NewGuid(),
        Status = EnvelopeStatus.InProgress,
    };

    private static Signer Signed(string role) => new()
    {
        Id = Guid.NewGuid(),
        Role = role,
        Name = "Alice",
        Status = SigningRecipientStatus.Signed,
        SignatureImage = TinyPng,
    };

    /// <summary>
    /// 旧快照里可能还躺着没有角色的签名字段（建模板那侧现在拦了，存量不经那道门）。
    /// 密封器找不到该盖谁的图时不能一声不吭地跳过：成品少一个签名而外观完整，
    /// 这行 Warning 是唯一的症状。
    /// </summary>
    [Fact]
    public async Task Sealing_warns_when_a_signature_field_has_no_role()
    {
        var (sealer, stamper, logger) = Create();
        var snapshot = new SigningSnapshot { Fields = [SignatureField(role: null)] };

        var result = await sealer.SealAsync(Envelope(), snapshot, new Dictionary<string, string?>(), [Signed("Client")]);

        result.Succeeded.ShouldBeTrue(result.Message);
        stamper.LastStamp!.Stamps.ShouldBeEmpty();
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("sig"));
    }

    /// <summary>角色对得上就盖图，不留 Warning。</summary>
    [Fact]
    public async Task Sealing_stamps_the_signature_of_the_matching_role()
    {
        var (sealer, stamper, logger) = Create();
        var snapshot = new SigningSnapshot { Fields = [SignatureField(role: "Client")] };

        var result = await sealer.SealAsync(Envelope(), snapshot, new Dictionary<string, string?>(), [Signed("client")]);

        result.Succeeded.ShouldBeTrue(result.Message);
        stamper.LastStamp!.Stamps.OfType<PdfImageStamp>().Count().ShouldBe(1);
        logger.Entries.ShouldNotContain(e => e.Level == LogLevel.Warning);
    }

    // ── 文本字段装进字段框 ────────────────────────────────────────────────

    /// <summary>A4 竖版（pt）。</summary>
    private const double PageWidth = 595d;
    private const double PageHeight = 842d;

    /// <summary>读得出页面尺寸的密封器：文本字段按框宽换行要用它。</summary>
    private static (SigningSealer Sealer, RecordingStamper Stamper) CreateWithA4Page()
    {
        var (_, stamper, logger) = Create();
        var inspector = new Mock<IPdfInspector>();
        inspector.Setup(i => i.GetInfo(It.IsAny<byte[]>())).Returns(new PdfInfo([new PdfPageInfo(1, PageWidth, PageHeight)]));

        var files = new Mock<IFileStorageService>(MockBehavior.Loose);
        files.Setup(f => f.GetAsync(It.IsAny<Guid>()))
            .ReturnsAsync(() => Result.Success<Stream>(new MemoryStream([0x25, 0x50, 0x44, 0x46])));
        files.Setup(f => f.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(() => Result.Success(new FileRecord { Id = Guid.NewGuid() }));

        return (new SigningSealer(stamper, inspector.Object, files.Object, logger), stamper);
    }

    /// <summary>一个约 268 pt 宽、34 pt 高的文本框（字段字号不指定）。</summary>
    private static SnapshotField TextField(decimal? fontSize = null, SigningFieldType type = SigningFieldType.Text) => new()
    {
        Key = "address",
        Label = "Address",
        Type = type,
        RecipientRole = "Client",
        PlacementMode = FieldPlacementMode.Absolute,
        Page = 1,
        X = 0.1m,
        Y = 0.5m,
        W = 0.45m,
        H = 0.04m,
        FontSize = fontSize,
    };

    private static async Task<List<PdfTextStamp>> SealTextAsync(SigningSealer sealer, RecordingStamper stamper, SnapshotField field, string value)
    {
        var snapshot = new SigningSnapshot { Fields = [field] };
        var result = await sealer.SealAsync(Envelope(), snapshot, new Dictionary<string, string?> { [field.Key] = value }, [Signed("Client")]);
        result.Succeeded.ShouldBeTrue(result.Message);
        return stamper.LastStamp!.Stamps.OfType<PdfTextStamp>().ToList();
    }

    /// <summary>一行放得下的值（绝大多数）与此前逐字一致：框内一行、字段字号（默认 10）。</summary>
    [Fact]
    public async Task A_value_that_fits_is_stamped_as_one_line_in_the_box()
    {
        var (sealer, stamper) = CreateWithA4Page();
        var field = TextField();

        var stamps = await SealTextAsync(sealer, stamper, field, "Alice Smith");

        var stamp = stamps.ShouldHaveSingleItem();
        stamp.Text.ShouldBe("Alice Smith");
        stamp.FontSize.ShouldBe(10d);
        stamp.Rect.ShouldBe(new NormalizedRect(0.1, 0.5, 0.45, 0.04));
    }

    /// <summary>
    /// 盖章实现不换行也不裁剪：一段 150 字的地址此前单行穿过相邻内容、越过页边，而 FieldValue 里存的是全文。
    /// 现在按框宽换行、行数装不进框高时缩小字号，每一行都在框的宽度之内，整段在框的高度之内。
    /// </summary>
    [Fact]
    public async Task A_long_value_is_wrapped_and_shrunk_to_fit_the_box()
    {
        var (sealer, stamper) = CreateWithA4Page();
        var field = TextField();
        var value = string.Join(' ', Enumerable.Repeat("Suite 1200, 100 King Street West, Toronto ON", 3));

        var stamps = await SealTextAsync(sealer, stamper, field, value);

        stamps.Count.ShouldBeGreaterThan(1);
        var boxWidth = (double)field.W * PageWidth;
        var boxBottom = (double)(field.Y + field.H);
        foreach (var line in stamps)
        {
            line.FontSize.ShouldBeLessThanOrEqualTo(10d);
            line.FontSize.ShouldBeGreaterThanOrEqualTo(6d);
            TestTextMetrics.Width(line.Text, line.FontSize).ShouldBeLessThanOrEqualTo(boxWidth);
            line.Rect.X.ShouldBe((double)field.X);
            line.Rect.Y.ShouldBeGreaterThanOrEqualTo((double)field.Y);
            (line.Rect.Y + line.Rect.Height).ShouldBeLessThanOrEqualTo(boxBottom + 1e-9);
        }

        // 一个字都不丢。
        string.Join(' ', stamps.Select(s => s.Text)).ShouldBe(value);
    }

    [Fact]
    public async Task Line_breaks_in_the_value_start_new_lines()
    {
        var (sealer, stamper) = CreateWithA4Page();

        var stamps = await SealTextAsync(sealer, stamper, TextField(), "100 King St W\r\nToronto ON");

        stamps.Select(s => s.Text).ShouldBe(["100 King St W", "Toronto ON"]);
        stamps[1].Rect.Y.ShouldBeGreaterThan(stamps[0].Rect.Y);
    }

    /// <summary>字段指定的字号是上限：放得下照用，放不下才缩小。</summary>
    [Fact]
    public async Task An_explicit_font_size_is_kept_when_the_value_fits()
    {
        var (sealer, stamper) = CreateWithA4Page();

        var stamps = await SealTextAsync(sealer, stamper, TextField(fontSize: 14m), "Alice");

        stamps.ShouldHaveSingleItem().FontSize.ShouldBe(14d);
    }

    /// <summary>
    /// 缩到下限仍装不下：全部画出（多出的行越出框底），不丢字：成品要能读出完整的值。
    /// </summary>
    [Fact]
    public async Task A_value_too_long_even_at_the_minimum_size_is_kept_whole()
    {
        var (sealer, stamper) = CreateWithA4Page();
        var value = string.Join(' ', Enumerable.Repeat("terms", 400));

        var stamps = await SealTextAsync(sealer, stamper, TextField(), value);

        stamps.ShouldAllBe(s => s.FontSize == 6d);
        string.Join(' ', stamps.Select(s => s.Text)).ShouldBe(value);
    }

    [Theory]
    [InlineData("true", "X")]
    [InlineData("1", "X")]
    [InlineData("false", "")]
    public async Task A_checkbox_is_stamped_with_the_same_predicate_the_submit_check_uses(string value, string expected)
    {
        var (sealer, stamper) = CreateWithA4Page();

        var stamps = await SealTextAsync(sealer, stamper, TextField(type: SigningFieldType.Checkbox), value);

        stamps.ShouldHaveSingleItem().Text.ShouldBe(expected);
    }
}
