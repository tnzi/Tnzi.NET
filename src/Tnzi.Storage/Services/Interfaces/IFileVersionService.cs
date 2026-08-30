namespace Tnzi.Storage.Services;

/// <summary>
/// 文件版本管理服务接口
/// </summary>
/// <remarks>
/// 版本必须通过 <see cref="CreateVersionAsync"/> 显式创建；普通文件覆盖上传不会自动产生新版本，
/// 以避免改变 <c>FileStorageService.SaveAsync</c> 的去重语义。
/// Versions are created explicitly via <see cref="CreateVersionAsync"/>; an overwriting upload does not
/// automatically create a version, so the dedup semantics of the save path remain unchanged.
///
/// ★ 契约留在父模块、实现在 <c>Tnzi.Storage.Workspace</c>（理由同 <see cref="IFileShareService"/>：
/// 版本端点长在父模块的 <c>files</c> 路由上）。故签名里不出现 <c>FileVersion</c> 实体 ——
/// 它随 <c>Storage_Version</c> 表搬进了子模块，这里一律走 <see cref="FileVersionDto"/>。
/// <c>FileRecord</c> 仍可出现：那是父模块自己的核心表。
/// </remarks>
public interface IFileVersionService
{
    /// <summary>
    /// 创建文件版本
    /// </summary>
    Task<Result<FileVersionDto>> CreateVersionAsync(Guid fileId, Stream stream, string? description = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文件版本列表
    /// </summary>
    Task<Result<IEnumerable<FileVersionDto>>> GetVersionsAsync(Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复指定版本
    /// </summary>
    Task<Result<FileRecord>> RestoreVersionAsync(Guid fileId, int version, CancellationToken cancellationToken = default);

    /// <summary>
    /// 只读获取指定版本的文件内容流（不改变当前版本指针）。
    /// Read-only download of a specific version's content without mutating the current version.
    /// </summary>
    Task<Result<Stream>> GetVersionContentAsync(Guid fileId, int version, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除指定版本（记录 + 物理文件）。禁止删除当前版本。
    /// Delete a specific version (record + physical file). Deleting the current version is not allowed.
    /// </summary>
    Task<Result> DeleteVersionAsync(Guid fileId, int version, CancellationToken cancellationToken = default);
}
