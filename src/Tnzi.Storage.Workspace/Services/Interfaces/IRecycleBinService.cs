namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// 文件空间的回收站：删除放进来、可还原、可永久删除、过期自动清除。
/// </summary>
/// <remarks>
/// <para>
/// 每个用户一个回收站。删掉的东西进<b>所有者</b>的回收站（管理员替用户删的也是），列表、还原、永久删除都只对自己的回收站。
/// 回收站里的文件在清除之前一直计入所有者的配额 —— 字节还在，回收站本来就是为了能拿回来。
/// </para>
/// <para>
/// 这里的「删除」只属于文件空间。父模块的 <c>DELETE files/{id}</c> 与目录服务的 <c>DeleteAsync</c>（只删空目录）语义不变；
/// 文件空间的界面删东西应当走这里。
/// </para>
/// </remarks>
public interface IRecycleBinService
{
    /// <summary>
    /// 把文件与目录（连同其中的一切）放进它们所有者的回收站。要能改这些文件 / 目录（自己的，或持对应权限码），
    /// 且它们此刻在文件空间里；任何一项不满足整批不动，答 404。
    /// </summary>
    Task<Result> MoveToRecycleBinAsync(IReadOnlyCollection<Guid>? fileIds, IReadOnlyCollection<Guid>? folderIds, CancellationToken cancellationToken = default);

    /// <summary>当前用户回收站里的东西，最新删除的在前。</summary>
    Task<Result<IPagedList<RecycleBinEntryDto>>> GetEntriesAsync(RecycleBinQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 还原：回到原来的目录；那个目录已经不在（删了、进了回收站）就回到根目录。目录还原时原位置已有同名目录的，
    /// 名字后面加序号（结果里会说）。条目必须都在当前用户的回收站里，否则整批答 404。
    /// </summary>
    Task<Result<RecycleBinRestoreResult>> RestoreAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 永久删除回收站里的这几项。没有别的记录引用的文件连字节一起删掉；还被别处（如聊天消息）引用的文件留下，
    /// 只是不再出现在文件空间里。返回处理的条目数。
    /// </summary>
    Task<Result<int>> PurgeAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken = default);

    /// <summary>清空当前用户的回收站，返回处理的条目数。</summary>
    Task<Result<int>> EmptyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 以<b>系统身份</b>清除放进回收站超过 <c>Storage:RecycleBin:RetentionDays</c> 天的条目（所有用户、所有租户），
    /// 最多 <paramref name="maxEntries"/> 条，返回清除的条数。<c>RetentionDays</c> 为 0 时什么都不做。
    /// 由后台清理调用，不做授权判定。
    /// </summary>
    Task<int> PurgeExpiredAsync(int maxEntries, CancellationToken cancellationToken = default);
}
