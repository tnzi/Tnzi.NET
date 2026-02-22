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
    /// <param name="fileName">文件名</param>
    /// <param name="stream">文件流</param>
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
}
