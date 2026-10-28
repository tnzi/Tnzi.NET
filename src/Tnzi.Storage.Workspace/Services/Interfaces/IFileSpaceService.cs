namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// 用户自己的文件空间：浏览一个目录、按名字找文件、把文件存进某个目录。
/// </summary>
/// <remarks>
/// <para>
/// 文件空间是每个用户自己的：目录路径按所有者唯一（两个人都可以有 <c>/Documents</c>），列表与搜索只看得到自己的目录与文件，
/// 别人的目录名、文件名不会出现在任何一个回答里。「哪些文件在文件空间里」的判据在 <see cref="FileSpaceQueries"/>。
/// </para>
/// <para>
/// 写进文件空间的文件一律是<b>正式记录</b>、<b>不做 MD5 去重</b>：同一份 PDF 存进两个目录就是两个文件，
/// 而去重会让两次上传拿到同一条记录、第二次归档把它从第一个目录里挪走。字节记在所有者名下，受他的配额约束。
/// 大文件走分片上传，完成时带上 <c>folderId</c>（<c>POST files/upload/chunk/{id}/complete</c>）。
/// </para>
/// </remarks>
public interface IFileSpaceService
{
    /// <summary>
    /// 列出当前用户的一个目录（或根目录）：先目录、后文件，一起分页，按名字 / 时间 / 大小排序。
    /// 目录不存在、不是自己的、或在回收站里，一律 404。
    /// </summary>
    Task<Result<FileSpaceListingDto>> ListAsync(FileSpaceListRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 在当前用户的整个文件空间里按名字（与扩展名 / 内容类型）找文件。回收站里的文件不在结果里。
    /// </summary>
    Task<Result<IPagedList<FileSpaceEntryDto>>> SearchAsync(FileSpaceSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 把一个文件存进当前用户的某个目录（<paramref name="folderId"/> 为 null = 根目录）：正式记录、不去重、计入配额。
    /// 单次请求受 <c>Storage:MaxFileSize</c> 约束，更大的文件走分片上传。
    /// </summary>
    Task<Result<FileRecord>> UploadFileAsync(Guid? folderId, string fileName, Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// 以<b>系统身份</b>替 <paramref name="ownerId"/> 把一个文件存进他的某个目录（AI 产出的摘要、应用生成的报表）。
    /// 文件归他所有、记在他的配额上；目录必须是他的。
    /// </summary>
    /// <remarks>
    /// 不做授权判定，后台任务里也能用（那里没有当前用户）。<b><paramref name="ownerId"/> 绝不能取自请求输入</b>：
    /// 那等于让调用方把文件塞进任意一个用户的空间。要先建好目录，用 <see cref="IFileFolderService.EnsurePathAsync"/>。
    /// </remarks>
    Task<Result<FileRecord>> SaveForOwnerAsync(Guid ownerId, Guid? folderId, string fileName, Stream stream, CancellationToken cancellationToken = default);
}
