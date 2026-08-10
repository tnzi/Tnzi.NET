using Tnzi.Security;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 上传净化管线。
/// </summary>
/// <remarks>
/// 三条要守住的性质：<strong>不注册即零影响</strong>、<strong>拒绝要真的挡住落库</strong>、
/// <strong>替换内容后 MD5 必须基于替换后的字节</strong>（否则去重与完整性校验都会指向错误的内容）。
/// </remarks>
public class UploadSanitizationTests : StorageIntegrationTestBase
{
    private static StorageOptions Options(bool enableMd5 = true) => new()
    {
        MaxFileSize = 50 * 1024 * 1024,
        AllowedExtensions = [".png", ".jpg", ".txt", ".pdf"],
        AutoGenerateThumbnail = false,
        EnableMd5Validation = enableMd5
    };

    [Fact]
    public async Task NoSanitizerRegistered_UploadBehavesExactlyAsBefore()
    {
        // 「可选能力」的核心断言：不注册就什么都不发生。
        var service = CreateStorageService(Options());
        var content = "plain content"u8.ToArray();

        var result = await service.SaveAsync("note.txt", new MemoryStream(content));

        Assert.True(result.Succeeded);
        Assert.Equal(content.LongLength, result.Data!.Size);
    }

    [Fact]
    public async Task RejectingSanitizer_FailsTheUploadAndStoresNothing()
    {
        var sanitizer = new RecordingSanitizer { RejectWith = "Simulated malware detected." };
        var service = CreateStorageService(Options(), sanitizers: [sanitizer]);

        var result = await service.SaveAsync("payload.txt", new MemoryStream("bad"u8.ToArray()));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Contains("Simulated malware", result.Message, StringComparison.Ordinal);
        Assert.Empty(await DbContext.Set<FileRecord>().ToListAsync());
    }

    [Fact]
    public async Task ReplacingSanitizer_StoresSanitizedBytes_AndHashesThem()
    {
        // 模拟「剥离元数据后重编码」：内容变了，落库的大小与哈希都必须跟着变。
        var original = "ORIGINAL-WITH-GPS-METADATA"u8.ToArray();
        var cleaned = "CLEANED"u8.ToArray();
        var sanitizer = new RecordingSanitizer { ReplaceWith = cleaned };
        var service = CreateStorageService(Options(), sanitizers: [sanitizer]);

        var result = await service.SaveAsync("photo.jpg", new MemoryStream(original));

        Assert.True(result.Succeeded);
        Assert.Equal(cleaned.LongLength, result.Data!.Size);

        // MD5 必须是净化后内容的，否则去重会把它当成另一份文件、完整性校验永远失败。
        var expectedHash = await HashHelper.GetMd5Async(new MemoryStream(cleaned));
        Assert.Equal(expectedHash, result.Data.Md5Hash);

        // 存储里躺着的也必须是净化后的字节。
        Assert.NotNull(result.Data.Path);
        await using var stored = await Storage.DownloadAsync(result.Data.Path);
        using var buffer = new MemoryStream();
        await stored.CopyToAsync(buffer);
        Assert.Equal(cleaned, buffer.ToArray());
    }

    [Fact]
    public async Task Sanitizers_RunInOrderAndEachSeesTheStreamFromTheStart()
    {
        // 第二个净化器读到的必须是完整内容，而不是上一个读剩下的半截。
        var first = new RecordingSanitizer { OrderValue = 10 };
        var second = new RecordingSanitizer { OrderValue = 20 };

        // 注册顺序与 Order 相反，验证排序真的按 Order 而不是按注册顺序
        var service = CreateStorageService(Options(), sanitizers: [second, first]);
        var content = "0123456789"u8.ToArray();

        var result = await service.SaveAsync("data.txt", new MemoryStream(content));

        Assert.True(result.Succeeded);
        Assert.True(first.InvokedAt < second.InvokedAt, "Order 小的必须先执行");
        Assert.Equal(content.Length, first.BytesVisible);
        Assert.Equal(content.Length, second.BytesVisible);
    }

    [Fact]
    public async Task ReplacementStream_IsDisposedByThePipeline()
    {
        // 净化器交出替换流的所有权后就不管了，管线必须负责释放，否则每次上传泄漏一个流。
        var replacement = new TrackingMemoryStream("cleaned"u8.ToArray());
        var sanitizer = new RecordingSanitizer { ReplacementStream = replacement };
        var service = CreateStorageService(Options(), sanitizers: [sanitizer]);

        await service.SaveAsync("photo.jpg", new MemoryStream("original"u8.ToArray()));

        Assert.True(replacement.WasDisposed, "管线必须释放它接管的替换流");
    }

    [Fact]
    public async Task ContentSignatureSanitizer_RejectsDisguisedFile()
    {
        // 把可执行内容命名成 .png 上传。
        var service = CreateStorageService(Options(), sanitizers: [new ContentSignatureSanitizer()]);

        var result = await service.SaveAsync("innocent.png", new MemoryStream("MZ\0executable"u8.ToArray()));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    [Fact]
    public async Task ContentSignatureSanitizer_AcceptsGenuineFile()
    {
        var service = CreateStorageService(Options(), sanitizers: [new ContentSignatureSanitizer()]);
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];

        var result = await service.SaveAsync("real.png", new MemoryStream(png));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ContentSignatureSanitizer_PassesUnknownExtensionsThrough()
    {
        // 只拒绝「确知不符」的；不认识的格式一律放行，否则每加一种新格式都得改框架。
        var service = CreateStorageService(Options(), sanitizers: [new ContentSignatureSanitizer()]);

        var result = await service.SaveAsync("notes.txt", new MemoryStream("anything at all"u8.ToArray()));

        Assert.True(result.Succeeded);
    }

    private sealed class RecordingSanitizer : IUploadSanitizer
    {
        private static int _counter;

        public int OrderValue { get; init; } = 100;
        public string? RejectWith { get; init; }
        public byte[]? ReplaceWith { get; init; }
        public Stream? ReplacementStream { get; init; }

        public int InvokedAt { get; private set; }
        public int BytesVisible { get; private set; }

        public int Order => OrderValue;

        public async Task<UploadSanitizationResult> SanitizeAsync(
            UploadSanitizationContext context,
            CancellationToken cancellationToken = default)
        {
            InvokedAt = Interlocked.Increment(ref _counter);

            using var probe = new MemoryStream();
            await context.Content.CopyToAsync(probe, cancellationToken);
            BytesVisible = (int)probe.Length;

            if (RejectWith != null)
            {
                return UploadSanitizationResult.Reject(RejectWith);
            }

            if (ReplacementStream != null)
            {
                return UploadSanitizationResult.Replaced(ReplacementStream);
            }

            return ReplaceWith != null
                ? UploadSanitizationResult.Replaced(new MemoryStream(ReplaceWith))
                : UploadSanitizationResult.Unchanged();
        }
    }

    private sealed class TrackingMemoryStream : MemoryStream
    {
        public TrackingMemoryStream(byte[] buffer) : base(buffer, writable: false) { }

        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            WasDisposed = true;
            return base.DisposeAsync();
        }
    }
}
