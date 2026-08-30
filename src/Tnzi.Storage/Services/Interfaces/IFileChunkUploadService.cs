namespace Tnzi.Storage.Services;

/// <summary>
/// 文件分块上传服务接口。
/// </summary>
/// <remarks>
/// ★ 契约留在父模块、实现在 <c>Tnzi.Storage.Workspace</c>（理由同 <see cref="IFileShareService"/>）。
/// 故签名里不出现 <c>FileUploadSession</c> / <c>FileChunk</c> 实体 —— 它们随
/// <c>Storage_UploadSession</c> 与 <c>Storage_Chunk</c> 两张表搬进了子模块。
/// </remarks>
public interface IFileChunkUploadService
{
    /// <summary>
    /// 初始化分块上传会话
    /// </summary>
    Task<Result<FileUploadSessionDto>> InitiateChunkedUploadAsync(string fileName, long totalSize, int chunkSize = 5 * 1024 * 1024, string? md5Hash = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传分块
    /// </summary>
    Task<Result<FileChunkDto>> UploadChunkAsync(Guid uploadSessionId, int chunkIndex, Stream chunkStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// 完成分块上传
    /// </summary>
    Task<Result<FileRecord>> CompleteChunkedUploadAsync(Guid uploadSessionId, bool isTemporary = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消分块上传
    /// </summary>
    Task<Result> CancelChunkedUploadAsync(Guid uploadSessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取上传进度
    /// </summary>
    Task<Result<FileUploadProgress>> GetUploadProgressAsync(Guid uploadSessionId, CancellationToken cancellationToken = default);
}
