namespace Tnzi.Storage.Services;

/// <summary>
/// 文件版本管理服务接口
/// </summary>
public interface IFileVersionService
{
    /// <summary>
    /// 创建文件版本
    /// </summary>
    Task<Result<FileVersion>> CreateVersionAsync(Guid fileId, Stream stream, string? description = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文件版本列表
    /// </summary>
    Task<Result<IEnumerable<FileVersion>>> GetVersionsAsync(Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复指定版本
    /// </summary>
    Task<Result<FileRecord>> RestoreVersionAsync(Guid fileId, int version, CancellationToken cancellationToken = default);
}
