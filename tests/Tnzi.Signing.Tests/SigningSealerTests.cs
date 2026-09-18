using Microsoft.Extensions.Logging;
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
}
