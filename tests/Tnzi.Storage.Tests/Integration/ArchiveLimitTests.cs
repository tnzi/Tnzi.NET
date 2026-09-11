using System.IO.Compression;
using Tnzi.Storage.Helpers;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 压缩 / 解压的资源上限（<see cref="ArchiveOptions"/>）。
/// </summary>
/// <remarks>
/// 两个端点此前只过扩展名闸门：没有条目数上限、没有解压后总体积上限、单条也不受 MaxFileSize 约束。
/// 一个 zip bomb 或一次几万个 id 的打包，登录即可发起。这里把三道上限各钉一条，
/// 并钉住「总量越界作废整包时不留孤儿对象」。
/// </remarks>
public class ArchiveLimitTests : StorageIntegrationTestBase
{
    private (RecordingFileStorage Recorder, FileStorageService Service) Build(StorageOptions options)
    {
        var recorder = new RecordingFileStorage(Storage);
        return (recorder, CreateStorageService(recorder, options));
    }

    private StorageOptions Limits(int maxEntries = 1000, long maxTotal = 1024L * 1024 * 1024, long maxFileSize = 50 * 1024 * 1024)
        => new()
        {
            MaxFileSize = maxFileSize,
            AllowedExtensions = StorageOptions.AllowedExtensions,
            AutoGenerateThumbnail = false,
            Archive = new ArchiveOptions { MaxEntries = maxEntries, MaxTotalExtractedBytes = maxTotal },
        };

    private static byte[] Zip(params (string Name, int Bytes)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(new byte[bytes]);
            }
        }

        return buffer.ToArray();
    }

    // ── 压缩 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Compress_RefusesMoreFilesThanMaxEntries_BeforeReadingAnything()
    {
        var (recorder, service) = Build(Limits(maxEntries: 2));
        var a = await CreateStoredFileAsync("a.txt", [1]);
        var b = await CreateStoredFileAsync("b.txt", [2]);
        var c = await CreateStoredFileAsync("c.txt", [3]);

        var result = await service.CompressAsync([a.Id, b.Id, c.Id], "too-many.zip");

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Empty(recorder.UploadedKeys);
    }

    [Fact]
    public async Task Compress_WithinMaxEntries_StillWorks()
    {
        var (_, service) = Build(Limits(maxEntries: 2));
        var a = await CreateStoredFileAsync("a.txt", [1]);
        var b = await CreateStoredFileAsync("b.txt", [2]);

        var result = await service.CompressAsync([a.Id, b.Id]);

        Assert.True(result.Succeeded, result.Message);
    }

    // ── 解压：条目数 ──────────────────────────────────────────────────────

    [Fact]
    public async Task Decompress_RefusesAnArchiveWithMoreEntriesThanMaxEntries_AndExtractsNothing()
    {
        var (recorder, service) = Build(Limits(maxEntries: 2));
        var archive = await CreateStoredFileAsync("many.zip", Zip(("a.txt", 1), ("b.txt", 1), ("c.txt", 1)));

        var result = await service.DecompressAsync(archive.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Empty(recorder.UploadedKeys);
        Assert.Single(DbContext.FileRecords);   // 只有那个 zip 本身
    }

    // ── 解压：单条大小 ────────────────────────────────────────────────────

    /// <summary>解出来的每一条都是一份独立文件记录，与直传过同一道 MaxFileSize；单条越界跳过，其余照解。</summary>
    [Fact]
    public async Task Decompress_SkipsAnEntryLargerThanMaxFileSize_AndExtractsTheRest()
    {
        var (recorder, service) = Build(Limits(maxFileSize: 10));
        var archive = await CreateStoredFileAsync("mixed.zip", Zip(("small.txt", 4), ("huge.txt", 40), ("tiny.txt", 2)));

        var result = await service.DecompressAsync(archive.Id);

        Assert.True(result.Succeeded, result.Message);
        var names = result.Data!.Select(r => r.OriginalName).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "small.txt", "tiny.txt" }, names);
        Assert.Equal(2, recorder.UploadedKeys.Count);
    }

    // ── 解压：总体积 ──────────────────────────────────────────────────────

    /// <summary>总量越界作废整次解包：一条记录都不落，已经交给 provider 的对象全部删掉。</summary>
    [Fact]
    public async Task Decompress_AbortsWhenTheTotalExceedsTheBudget_AndLeavesNoOrphans()
    {
        var (recorder, service) = Build(Limits(maxTotal: 20, maxFileSize: 10));
        var archive = await CreateStoredFileAsync("bomb.zip", Zip(("a.txt", 8), ("b.txt", 8), ("c.txt", 8)));

        var result = await service.DecompressAsync(archive.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Single(DbContext.FileRecords);   // 只有那个 zip 本身

        // 前两条已经上传，第三条把总量推过 20 → 两个对象都要被删掉。
        Assert.Equal(2, recorder.UploadedPaths.Count);
        Assert.Equal(recorder.UploadedPaths.OrderBy(p => p), recorder.DeletedPaths.OrderBy(p => p));
        foreach (var path in recorder.UploadedPaths)
            Assert.False(await Storage.ExistsAsync(path), $"orphan left behind: {path}");
    }

    [Fact]
    public async Task Decompress_WithinEveryLimit_ExtractsEverything()
    {
        var (_, service) = Build(Limits(maxEntries: 3, maxTotal: 30, maxFileSize: 10));
        var archive = await CreateStoredFileAsync("ok.zip", Zip(("a.txt", 8), ("b.txt", 8), ("c.txt", 8)));

        var result = await service.DecompressAsync(archive.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3, result.Data!.Count());
    }

    // ── 边复制边数 ────────────────────────────────────────────────────────

    /// <summary>头可以撒谎：判据必须是复制时数出来的字节。这里直接测那个计数器。</summary>
    [Fact]
    public async Task CopyBounded_StopsAtTheLimit_WithoutReadingTheRest()
    {
        // 源比一次读取的缓冲大得多：越界发生在第一块之后，后面的块一个都不该再读。
        var source = new MemoryStream(new byte[4 * 1024 * 1024]);
        using var destination = new MemoryStream();

        var copied = await StreamLimitHelper.CopyBoundedAsync(source, destination, limit: 10);

        Assert.Equal(-1, copied);
        Assert.True(source.Position < source.Length, "the remainder of an oversized source must not be read to the end");
    }

    [Fact]
    public async Task CopyBounded_CopiesEverything_WhenWithinTheLimit()
    {
        var source = new MemoryStream([1, 2, 3, 4, 5]);
        using var destination = new MemoryStream();

        var copied = await StreamLimitHelper.CopyBoundedAsync(source, destination, limit: 5);

        Assert.Equal(5, copied);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, destination.ToArray());
    }
}
