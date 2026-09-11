using System.Security.Cryptography;
using Tnzi.Storage.Helpers;

namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 父模块那条不变量（<b>存储键永远由服务端生成</b>）在本模块两条写路径上的体现：
/// 分片完成与建新版本。理由与父测试项目的 <c>StorageKeyInvariantTests</c> 相同，
/// 这里只写本模块独有的两条。
/// </summary>
/// <remarks>
/// 分片完成是三条漂移路径里最要紧的一条：文档把它推荐为大文件的通道，而它此前直接拿
/// <c>InitiateChunkedUploadAsync</c> 收到的 <c>fileName</c> 当键 —— 两个用户同一天上传同名的
/// <c>report.pdf</c>，第二份静默覆盖第一份的字节，第一条记录的 MD5 与大小仍是旧值。
/// </remarks>
public class StorageKeyInvariantTests : WorkspaceIntegrationTestBase
{
    private FileChunkUploadService CreateChunkUploadService(RecordingFileStorage recorder)
        => new(
            new EFCoreRepository<WorkspaceTestDbContext, FileUploadSession, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<WorkspaceTestDbContext, FileChunk, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<WorkspaceTestDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider),
            recorder,
            new StaticOptionsMonitor<StorageOptions>(StorageOptions),
            ServiceProvider,
            new UploadGuard(new StaticOptionsMonitor<StorageOptions>(StorageOptions)));

    private FileVersionService CreateVersionService(RecordingFileStorage recorder)
        => new(
            new EFCoreRepository<WorkspaceTestDbContext, FileVersion, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<WorkspaceTestDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider),
            recorder,
            TestFileAccessAuthorizer.AllowAll(),
            ServiceProvider,
            new UploadGuard(new StaticOptionsMonitor<StorageOptions>(StorageOptions)));

    private async Task<string> Md5OfStoredAsync(string path)
    {
        using var stream = await Storage.DownloadAsync(path);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return Convert.ToHexString(MD5.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    private async Task<FileRecord> UploadInChunksAsync(FileChunkUploadService service, string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var session = (await service.InitiateChunkedUploadAsync(fileName, bytes.Length, chunkSize: 4)).Data!;
        for (var i = 0; i < session.TotalChunks; i++)
        {
            var slice = bytes.Skip(i * 4).Take(4).ToArray();
            var uploaded = await service.UploadChunkAsync(session.Id, i, new MemoryStream(slice));
            Assert.True(uploaded.Succeeded, uploaded.Message);
        }

        var completed = await service.CompleteChunkedUploadAsync(session.Id);
        Assert.True(completed.Succeeded, completed.Message);
        return completed.Data!;
    }

    [Fact]
    public async Task ChunkedUpload_StoresTheMergedFileUnderAGeneratedKey_AndKeepsTheClientNameForDisplay()
    {
        var recorder = new RecordingFileStorage(Storage);
        var service = CreateChunkUploadService(recorder);

        var record = await UploadInChunksAsync(service, "their-report.txt", "abcdefgh");

        Assert.True(StorageKeyHelper.IsGenerated(record.FileName), $"the merged file was stored under the client's name: {record.FileName}");
        Assert.Equal("their-report.txt", record.OriginalName);
        Assert.Equal(".txt", record.Extension);
        // 分片临时对象 + 合并后的那一个，全部是服务端生成的键。
        Assert.All(recorder.UploadedKeys, key =>
            Assert.True(StorageKeyHelper.IsGenerated(key), $"a client-supplied name reached the provider: {key}"));
        Assert.Contains(record.FileName, recorder.UploadedKeys);
    }

    /// <summary>同名分片上传两次：第一份的字节必须原样还在。</summary>
    [Fact]
    public async Task ChunkedUpload_TwiceWithTheSameName_DoesNotOverwriteTheFirstFile()
    {
        var recorder = new RecordingFileStorage(Storage);
        var service = CreateChunkUploadService(recorder);

        var first = await UploadInChunksAsync(service, "report.txt", "first-upload");
        var second = await UploadInChunksAsync(service, "report.txt", "second-upload-longer");

        Assert.NotEqual(first.Path, second.Path);
        Assert.Equal(first.Md5Hash, await Md5OfStoredAsync(first.Path!));
        Assert.Equal(second.Md5Hash, await Md5OfStoredAsync(second.Path!));
    }

    /// <summary>
    /// 版本键不派生自 <c>FileRecord.FileName</c>：修复之前那三条路径写下的记录 <c>FileName</c>
    /// 是调用方给的，派生等于把污染往下传。这里故意造一条那种形态的存量记录。
    /// </summary>
    [Fact]
    public async Task CreateVersion_StoresTheNewBytesUnderAGeneratedKey_EvenForALegacyRecordNamedByItsCaller()
    {
        var recorder = new RecordingFileStorage(Storage);
        var service = CreateVersionService(recorder);
        var legacy = await CreateStoredFileAsync("victim.txt", "v1"u8.ToArray());

        var version = await service.CreateVersionAsync(legacy.Id, new MemoryStream("v2"u8.ToArray()), "second");

        Assert.True(version.Succeeded, version.Message);
        var key = Assert.Single(recorder.UploadedKeys);
        Assert.True(StorageKeyHelper.IsGenerated(key), $"the version was stored under a key derived from the caller's name: {key}");
        Assert.EndsWith(".txt", key);
    }
}
