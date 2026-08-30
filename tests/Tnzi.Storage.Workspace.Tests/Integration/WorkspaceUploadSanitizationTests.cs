namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 净化管线在<b>分片上传完成</b>与<b>建新版本</b>两条写路径上的行为。
/// </summary>
/// <remarks>
/// 这两条路径此前一道闸门都不过，是 2026-08-23 那批加固的核心；用例随被加固的服务
/// 搬到本项目，内容一字未改。直传那条仍留在父测试项目。
/// </remarks>
public class WorkspaceUploadSanitizationTests : WorkspaceIntegrationTestBase
{
    private StorageOptions Options(bool enableMd5 = true) => new()
    {
        MaxFileSize = StorageOptions.MaxFileSize,
        AllowedExtensions = StorageOptions.AllowedExtensions,
        AutoGenerateThumbnail = false,
        EnableMd5Validation = enableMd5
    };

    [Fact]
    public async Task RejectingSanitizer_AlsoBlocksChunkedUploadCompletion()
    {
        var sanitizer = new RecordingSanitizer { RejectWith = "Simulated malware detected." };
        var service = CreateChunkUploadService(Options(), sanitizers: [sanitizer]);

        var init = await service.InitiateChunkedUploadAsync("payload.txt", 3, 3);
        Assert.True(init.Succeeded);

        var sid = init.Data!.Id;
        Assert.True((await service.UploadChunkAsync(sid, 0, new MemoryStream("bad"u8.ToArray()))).Succeeded);

        var result = await service.CompleteChunkedUploadAsync(sid);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Contains("Simulated malware", result.Message, StringComparison.Ordinal);
        Assert.Empty(await DbContext.Set<FileRecord>().ToListAsync());
    }

    /// <summary>
    /// 净化器改写内容后，分片完成落库的大小与哈希必须是**改写之后**那份字节的。
    /// </summary>
    /// <remarks>
    /// 记合并时的长度就等于「存的是新字节、写的是旧尺寸」——下载下来的文件与记录对不上，
    /// 而完整性校验会把它报成损坏。直传路径一直是对的（`SaveAsync` 重取净化后的长度），
    /// 分片这条是补闸门时才接上的，所以单独钉住。
    /// </remarks>
    [Fact]
    public async Task ChunkedUpload_RecordsTheSanitizedBytes_NotTheMergedOnes()
    {
        var cleaned = "CLEANED"u8.ToArray();
        var sanitizer = new RecordingSanitizer { ReplaceWith = cleaned };
        var service = CreateChunkUploadService(Options(enableMd5: false), sanitizers: [sanitizer]);

        var original = "ORIGINAL-WITH-METADATA"u8.ToArray();
        var init = await service.InitiateChunkedUploadAsync("photo.png", original.Length, original.Length);
        Assert.True(init.Succeeded);
        Assert.True((await service.UploadChunkAsync(init.Data!.Id, 0, new MemoryStream(original))).Succeeded);

        var result = await service.CompleteChunkedUploadAsync(init.Data.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(cleaned.LongLength, result.Data!.Size);
        Assert.NotEqual(original.LongLength, result.Data.Size);
    }

    /// <summary>
    /// 扩展名白名单在<b>建会话</b>那一刻就拦，客户端不必先把整个文件推上来才被拒。
    /// </summary>
    [Fact]
    public async Task ChunkedUpload_RejectsExtensionOutsideAllowList()
    {
        var service = CreateChunkUploadService(Options());

        var result = await service.InitiateChunkedUploadAsync("payload.exe", 3, 3);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    /// <summary>
    /// ★ 建新版本会把<b>父记录</b>重新指向新字节，所以它是这两条里更隐蔽的一条：
    /// 先正常传一张干净的 .png（过了扫描器与白名单），再对它 POST 一个新版本，
    /// 内容就换成了没被扫过的字节，而记录上仍写着 .png / image/png。
    /// </summary>
    [Fact]
    public async Task RejectingSanitizer_AlsoBlocksCreateVersion_AndLeavesTheParentRecordAlone()
    {
        var clean = await CreateStorageService(Options())
            .SaveAsync("photo.png", new MemoryStream("clean"u8.ToArray()));
        Assert.True(clean.Succeeded);

        var originalPath = clean.Data!.Path;
        var originalHash = clean.Data.Md5Hash;
        DbContext.ChangeTracker.Clear();

        var sanitizer = new RecordingSanitizer { RejectWith = "Simulated malware detected." };
        var service = CreateVersionService(sanitizers: [sanitizer], options: Options());

        var result = await service.CreateVersionAsync(clean.Data.Id, new MemoryStream("bad"u8.ToArray()));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);

        // 承重断言：父记录必须仍然指向旧字节。只返回错误却已经把 Path/Md5 改掉的实现
        // 会骗过前两条 —— 而那正是这个缺陷真正的危害。
        DbContext.ChangeTracker.Clear();
        var reloaded = DbContext.Set<FileRecord>().Single(f => f.Id == clean.Data.Id);
        Assert.Equal(originalPath, reloaded.Path);
        Assert.Equal(originalHash, reloaded.Md5Hash);

        // 被拒的上传不该留下任何版本行（含那条「Initial version」快照）。
        Assert.Empty(await DbContext.Set<FileVersion>().ToListAsync());
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
