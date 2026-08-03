namespace Tnzi.Storage.Providers;

/// <summary>
/// 云存储接口（抽象存储提供者）
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// 获取存储提供者名称
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// 上传文件
    /// </summary>
    /// <remarks>
    /// ★ 流的生命周期归调用方所有：实现**不得** dispose / close 传入的 <paramref name="stream"/>。
    /// 调用方在上传之后往往还要用这个流（最典型的是取 <c>Length</c> 写进文件记录），
    /// provider 提前关掉它，调用方就会拿到 <see cref="ObjectDisposedException"/>。
    /// 使用会自动接管流的 SDK 时必须显式关掉那个行为
    /// （例如 AWS SDK 的 <c>PutObjectRequest.AutoCloseStream</c> 默认为 <c>true</c>，须置为 <c>false</c>）。
    /// <para>
    /// 反过来，调用方也不应假设上传后流的位置：实现会把流读到末尾，需要复用时自行 <c>Seek</c>。
    /// </para>
    /// </remarks>
    /// <param name="fileName">文件名</param>
    /// <param name="stream">文件流（由调用方负责释放）</param>
    /// <param name="contentType">内容类型</param>
    /// <returns>文件路径或URL</returns>
    Task<string> UploadAsync(string fileName, Stream stream, string? contentType = null);

    /// <summary>
    /// 下载文件
    /// </summary>
    /// <param name="filePath">文件路径或URL</param>
    /// <returns>文件流</returns>
    Task<Stream> DownloadAsync(string filePath);

    /// <summary>
    /// 删除文件
    /// </summary>
    /// <param name="filePath">文件路径或URL</param>
    /// <returns>是否删除成功</returns>
    Task<bool> DeleteAsync(string filePath);

    /// <summary>
    /// 检查文件是否存在
    /// </summary>
    /// <param name="filePath">文件路径或URL</param>
    /// <returns>是否存在</returns>
    Task<bool> ExistsAsync(string filePath);

    /// <summary>
    /// 获取文件访问URL
    /// </summary>
    /// <param name="filePath">文件路径或URL</param>
    /// <param name="expiresIn">过期时间（秒），null表示永久有效</param>
    /// <returns>文件访问URL</returns>
    Task<string> GetUrlAsync(string filePath, int? expiresIn = null);

    /// <summary>
    /// 获取文件大小
    /// </summary>
    /// <param name="filePath">文件路径或URL</param>
    /// <returns>文件大小（字节）</returns>
    Task<long> GetFileSizeAsync(string filePath);

    /// <summary>
    /// 下载文件（支持 Range 请求，用于断点续传）
    /// </summary>
    /// <param name="filePath">文件路径或 URL</param>
    /// <param name="rangeStart">Range 起始位置（字节），null 表示从文件开头</param>
    /// <param name="rangeEnd">Range 结束位置（字节），null 表示到文件末尾</param>
    /// <returns>文件流和范围信息（Stream, Start, End, TotalLength）</returns>
    Task<(Stream Stream, long Start, long End, long TotalLength)> DownloadRangeAsync(
        string filePath,
        long? rangeStart = null,
        long? rangeEnd = null);

    /// <summary>
    /// Generate a presigned URL for direct upload or temporary public access.
    /// Default implementation returns null (not supported).
    /// Cloud providers (S3, R2, Azure) should override with real presigned URL generation.
    /// </summary>
    /// <param name="filePath">File path or key</param>
    /// <param name="expiresInSeconds">URL expiration in seconds (default 3600 = 1 hour)</param>
    /// <param name="httpMethod">HTTP method: GET for download, PUT for upload (default GET)</param>
    /// <returns>Presigned URL, or null if not supported by the provider</returns>
    Task<string?> GetPresignedUrlAsync(string filePath, int expiresInSeconds = 3600, string httpMethod = "GET")
        => Task.FromResult<string?>(null);

    /// <summary>
    /// Server-side copy an existing object to a new key within the same storage backend.
    /// Default implementation returns null (not supported) so callers fall back to download+upload.
    /// Cloud providers (S3, R2, Azure) should override to use native server-side copy, avoiding
    /// the bandwidth/memory cost of streaming large files through the application.
    /// </summary>
    /// <param name="sourcePath">Source object key/path</param>
    /// <param name="destFileName">Destination object key/path</param>
    /// <returns>The destination path on success, or null if the provider does not support server-side copy</returns>
    Task<string?> CopyAsync(string sourcePath, string destFileName)
        => Task.FromResult<string?>(null);
}
