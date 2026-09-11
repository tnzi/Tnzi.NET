using System.IO.Compression;
using Tnzi.Storage.Helpers;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 不变量：<b>存储键永远由服务端生成，绝不取自调用方给的名字。</b>
/// </summary>
/// <remarks>
/// <para>
/// 对象存储上键就是对象名、没有日期前缀，而 <c>PutObject</c> 默认覆盖。键若来自调用方，
/// 「我能读这条记录」（<c>FileRecordDto.fileName</c> 对能读的人可见）就升级成「我能替换它的字节」，
/// <c>CanWriteAsync</c> 整条判据链被绕过。本地磁盘上没有攻击也会出事：同一天两份同名文件，
/// 第二份把第一份截断写覆盖，第一条记录的 MD5 与大小仍是旧值，下载给出的却是别人的内容。
/// </para>
/// <para>
/// 2026-09-04 之前它在压缩 / 解压 / 分片完成三条写路径上各漂了一次，而没有任何测试变红 ——
/// 这条不变量当时只活在另外七处调用点的习惯里。这一组把它钉成断言：站在 provider 的位置
/// 记下每一个键（<see cref="RecordingFileStorage"/>），并用「同名传两次，第一份还在不在」
/// 这个最直接的后果做判据。
/// </para>
/// </remarks>
public class StorageKeyInvariantTests : StorageIntegrationTestBase
{
    private (RecordingFileStorage Recorder, FileStorageService Service) Build()
    {
        var recorder = new RecordingFileStorage(Storage);
        return (recorder, CreateStorageService(recorder));
    }

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var stream = entry.Open();
                var bytes = System.Text.Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        return buffer.ToArray();
    }

    private async Task<string> Md5OfStoredAsync(string path)
    {
        using var stream = await Storage.DownloadAsync(path);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return Convert.ToHexString(MD5.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    // ── 压缩 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Compress_HandsTheProviderAGeneratedKey_AndKeepsTheRequestedNameForDisplay()
    {
        var (recorder, service) = Build();
        var source = await CreateStoredFileAsync("a.txt", "AAA"u8.ToArray());

        var result = await service.CompressAsync([source.Id], "victim.zip");

        Assert.True(result.Succeeded, result.Message);
        var key = Assert.Single(recorder.UploadedKeys);
        Assert.True(StorageKeyHelper.IsGenerated(key), $"the archive was stored under the caller's name: {key}");
        Assert.Equal(key, result.Data!.FileName);
        Assert.Equal("victim.zip", result.Data.OriginalName);
        Assert.Equal(".zip", result.Data.Extension);
    }

    /// <summary>
    /// 同一个名字压两次：第一份必须原样还在。修复之前第二份在本地 provider 上把它截断写覆盖，
    /// 第一条记录的 MD5 与实际字节从此对不上。
    /// </summary>
    [Fact]
    public async Task Compress_TwiceWithTheSameName_DoesNotOverwriteTheFirstArchive()
    {
        var (_, service) = Build();
        var a = await CreateStoredFileAsync("a.txt", "AAA"u8.ToArray());
        var b = await CreateStoredFileAsync("b.txt", "BBBBBBBBBB"u8.ToArray());

        var first = (await service.CompressAsync([a.Id], "bundle.zip")).Data!;
        var second = (await service.CompressAsync([b.Id], "bundle.zip")).Data!;

        Assert.NotEqual(first.Path, second.Path);
        Assert.Equal(first.Md5Hash, await Md5OfStoredAsync(first.Path!));
        Assert.Equal(second.Md5Hash, await Md5OfStoredAsync(second.Path!));
    }

    // ── 解压 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Decompress_HandsTheProviderGeneratedKeys_AndKeepsEntryNamesForDisplay()
    {
        var (recorder, service) = Build();
        var archive = await CreateStoredFileAsync(
            "archive.zip", Zip(("report.txt", "one"), ("nested/dir/inner.txt", "two")));

        var result = await service.DecompressAsync(archive.Id);

        Assert.True(result.Succeeded, result.Message);
        var records = result.Data!.ToList();
        Assert.Equal(2, records.Count);
        Assert.Equal(2, recorder.UploadedKeys.Count);
        Assert.All(recorder.UploadedKeys, key =>
            Assert.True(StorageKeyHelper.IsGenerated(key), $"an extracted entry was stored under its own name: {key}"));
        Assert.Equal(recorder.UploadedKeys.OrderBy(k => k), records.Select(r => r.FileName).OrderBy(k => k));
        Assert.Equal(new[] { "inner.txt", "report.txt" }, records.Select(r => r.OriginalName).OrderBy(n => n));
    }

    /// <summary>
    /// 两个压缩包里都有一条 <c>report.txt</c>，内容不同：先解出来的那份必须还是它自己的字节。
    /// </summary>
    [Fact]
    public async Task Decompress_TwiceWithTheSameEntryName_DoesNotOverwriteTheFirstExtract()
    {
        var (_, service) = Build();
        var zipOne = await CreateStoredFileAsync("one.zip", Zip(("report.txt", "one")));
        var zipTwo = await CreateStoredFileAsync("two.zip", Zip(("report.txt", "two-two-two")));

        var first = (await service.DecompressAsync(zipOne.Id)).Data!.Single();
        var second = (await service.DecompressAsync(zipTwo.Id)).Data!.Single();

        Assert.NotEqual(first.Path, second.Path);
        Assert.Equal(first.Md5Hash, await Md5OfStoredAsync(first.Path!));
        Assert.Equal(second.Md5Hash, await Md5OfStoredAsync(second.Path!));
    }

    // ── 其余写路径：回归锁 ─────────────────────────────────────────────────

    /// <summary>
    /// 直传 / 批量直传 / 复制此前就用生成键，这里把它们一起锁住：三条漂移的路径当初正是
    /// 因为没有一条这样的断言才漂开的。调用方给的名字故意带上目录穿越。
    /// </summary>
    [Fact]
    public async Task EveryOtherWritePath_HandsTheProviderAGeneratedKey()
    {
        var (recorder, service) = Build();

        var saved = await service.SaveAsync("../../evil.txt", new MemoryStream("x"u8.ToArray()));
        Assert.True(saved.Succeeded, saved.Message);

        var many = await service.SaveManyAsync(
        [
            ("first.txt", (Stream)new MemoryStream("first"u8.ToArray())),
            ("second.txt", (Stream)new MemoryStream("second"u8.ToArray())),
        ]);
        Assert.True(many.Succeeded, many.Message);

        var copied = await service.CopyAsync(saved.Data!.Id, "renamed-by-caller.txt");
        Assert.True(copied.Succeeded, copied.Message);

        // 直传 1 + 批量 2 + 复制（Local 无服务端 copy：记一次 Copy 的目标键 + 一次回退上传）。
        Assert.True(recorder.UploadedKeys.Count >= 4, $"expected at least 4 keys, got {recorder.UploadedKeys.Count}");
        Assert.All(recorder.UploadedKeys, key =>
            Assert.True(StorageKeyHelper.IsGenerated(key), $"a caller-supplied name reached the provider: {key}"));

        Assert.Equal("../../evil.txt", saved.Data.OriginalName);
        Assert.Equal("renamed-by-caller.txt", copied.Data!.OriginalName);
    }
}
