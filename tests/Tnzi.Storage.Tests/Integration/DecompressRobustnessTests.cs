using System.IO.Compression;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 解压的两处边角：扩展名大小写，以及中途抛出时已上传的条目对象。
/// </summary>
public class DecompressRobustnessTests : StorageIntegrationTestBase
{
    private static byte[] Zip(params (string Name, int Bytes)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(new byte[bytes]);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>★ Extension 保留上传者文件名的大小写：Windows 上常见的 BACKUP.ZIP 此前被当成「不是 zip」拒掉。</summary>
    [Theory]
    [InlineData("BACKUP.ZIP")]
    [InlineData("Backup.Zip")]
    public async Task AnUpperCaseZipExtension_IsDecompressed(string name)
    {
        var archive = await CreateStoredFileAsync(name, Zip(("a.txt", 3), ("b.txt", 4)));

        var result = await CreateStorageService().DecompressAsync(archive.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2, result.Data!.Count());
    }

    /// <summary>
    /// ★★ 第三条上传时 provider 抛出：前两条已经交给 provider、还没有任何记录。此前异常直接上抛，
    /// 两个对象成了孤儿回收按记录永远看不见的东西；现在它们在异常离开之前被删掉。
    /// </summary>
    [Fact]
    public async Task AProviderFailureMidway_LeavesNoExtractedObjectsBehind()
    {
        var archive = await CreateStoredFileAsync("bundle.zip", Zip(("a.txt", 3), ("b.txt", 4), ("c.txt", 5)));
        var storage = new FailingOnUploadStorage(Storage, failOnUpload: 3);

        await Assert.ThrowsAsync<IOException>(() => CreateStorageService(storage).DecompressAsync(archive.Id));

        Assert.Equal(2, storage.UploadedPaths.Count);
        foreach (var path in storage.UploadedPaths)
            Assert.False(await Storage.ExistsAsync(path), $"orphan left behind: {path}");
        Assert.Single(DbContext.FileRecords);   // 只有那个 zip 本身
    }

    /// <summary>上传第 <c>failOnUpload</c> 次时抛 <see cref="IOException"/>，其余转给内层 provider。</summary>
    private sealed class FailingOnUploadStorage(IFileStorage inner, int failOnUpload) : IFileStorage
    {
        private int _uploads;

        public List<string> UploadedPaths { get; } = [];

        public string ProviderName => inner.ProviderName;

        public async Task<string> UploadAsync(string fileName, Stream stream, string? contentType = null)
        {
            if (++_uploads == failOnUpload)
                throw new IOException("Simulated provider failure");

            var path = await inner.UploadAsync(fileName, stream, contentType);
            UploadedPaths.Add(path);
            return path;
        }

        public Task<Stream> DownloadAsync(string filePath) => inner.DownloadAsync(filePath);

        public Task<bool> DeleteAsync(string filePath) => inner.DeleteAsync(filePath);

        public Task<bool> ExistsAsync(string filePath) => inner.ExistsAsync(filePath);

        public Task<string> GetUrlAsync(string filePath, int? expiresIn = null) => inner.GetUrlAsync(filePath, expiresIn);

        public Task<long> GetFileSizeAsync(string filePath) => inner.GetFileSizeAsync(filePath);

        public Task<(Stream Stream, long Start, long End, long TotalLength)> DownloadRangeAsync(
            string filePath, long? rangeStart = null, long? rangeEnd = null)
            => inner.DownloadRangeAsync(filePath, rangeStart, rangeEnd);
    }
}
