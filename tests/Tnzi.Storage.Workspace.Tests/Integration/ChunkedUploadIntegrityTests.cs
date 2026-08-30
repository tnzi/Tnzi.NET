using Tnzi.Exceptions;

namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// T6：分片合并时回读每一片的 MD5 做完整性校验，受 <c>Storage:EnableMd5Validation</c> 控制。
/// </summary>
/// <remarks>
/// 原在父测试项目的 <c>StorageConfigToggleTests</c> 里，随分片上传服务搬来，内容一字未改。
/// </remarks>
public class ChunkedUploadIntegrityTests : WorkspaceIntegrationTestBase
{
    // ---------------------------------------------------------------------
    // T6: 分片合并回读分片 MD5 校验
    // ---------------------------------------------------------------------

    [Fact]
    public async Task CompleteChunkedUpload_WithValidChunks_Succeeds()
    {
        var service = CreateChunkUploadService(); // EnableMd5Validation = true (默认)
        var session = (await service.InitiateChunkedUploadAsync("ok.txt", 6, 3)).Data!;
        await service.UploadChunkAsync(session.Id, 0, new MemoryStream("abc"u8.ToArray()));
        await service.UploadChunkAsync(session.Id, 1, new MemoryStream("def"u8.ToArray()));

        var result = await service.CompleteChunkedUploadAsync(session.Id);

        Assert.True(result.Succeeded);
        using var downloaded = await Storage.DownloadAsync(result.Data!.Path!);
        using var reader = new StreamReader(downloaded);
        Assert.Equal("abcdef", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task CompleteChunkedUpload_WithCorruptedChunk_FailsValidation()
    {
        var service = CreateChunkUploadService(); // EnableMd5Validation = true (默认)
        var session = (await service.InitiateChunkedUploadAsync("corrupt.txt", 6, 3)).Data!;
        await service.UploadChunkAsync(session.Id, 0, new MemoryStream("abc"u8.ToArray()));
        await service.UploadChunkAsync(session.Id, 1, new MemoryStream("def"u8.ToArray()));

        // 直接篡改第二个分片的物理存储内容，使其与记录的 Md5Hash 不符
        var chunk1 = DbContext.FileChunks.Single(c => c.UploadSessionId == session.Id && c.ChunkIndex == 1);
        await OverwriteStoredChunkAsync(chunk1.ChunkPath!, "xyz"u8.ToArray());

        var result = await service.CompleteChunkedUploadAsync(session.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Equal(ErrorCodes.VALIDATION_ERROR, result.ErrorCode);
        // 合并中止，不应产生文件记录
        Assert.Empty(DbContext.FileRecords);
    }

    [Fact]
    public async Task CompleteChunkedUpload_WithCorruptedChunk_AndMd5Disabled_Succeeds()
    {
        var options = new StorageOptions
        {
            MaxFileSize = StorageOptions.MaxFileSize,
            AllowedExtensions = StorageOptions.AllowedExtensions,
            AutoGenerateThumbnail = false,
            EnableMd5Validation = false
        };
        var service = CreateChunkUploadService(options);
        var session = (await service.InitiateChunkedUploadAsync("nomd5.txt", 6, 3)).Data!;
        await service.UploadChunkAsync(session.Id, 0, new MemoryStream("abc"u8.ToArray()));
        await service.UploadChunkAsync(session.Id, 1, new MemoryStream("def"u8.ToArray()));

        // 篡改分片内容；因 EnableMd5Validation=false，校验被跳过，合并仍成功
        var chunk1 = DbContext.FileChunks.Single(c => c.UploadSessionId == session.Id && c.ChunkIndex == 1);
        await OverwriteStoredChunkAsync(chunk1.ChunkPath!, "xyz"u8.ToArray());

        var result = await service.CompleteChunkedUploadAsync(session.Id);

        Assert.True(result.Succeeded);
        Assert.Single(DbContext.FileRecords);
    }

    /// <summary>
    /// LocalStorage 不支持原地覆盖（UploadAsync 总是写到当天日期目录），
    /// 因此通过反射定位分片物理文件并直接覆盖其内容以模拟损坏。
    /// </summary>
    private async Task OverwriteStoredChunkAsync(string chunkPath, byte[] newContent)
    {
        var basePathField = typeof(Tnzi.Storage.Providers.LocalStorage)
            .GetField("_basePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var basePath = (string)basePathField.GetValue(Storage)!;
        var fullPath = Path.Combine(basePath, chunkPath);
        await File.WriteAllBytesAsync(fullPath, newContent);
    }
}
