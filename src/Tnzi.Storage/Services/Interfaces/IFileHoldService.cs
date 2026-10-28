namespace Tnzi.Storage.Services;

/// <summary>
/// 以<b>系统身份</b>处理文件的隐式持有者（正式上传本身、MD5 复用、文件空间的那一份）。
/// </summary>
/// <remarks>
/// <para>
/// 给文件空间的「永久删除」用：用户删的是这个文件，而它可能同时挂在一条聊天消息上 —— 那一份属于消息，
/// 不在这里放（文件因此留下，结果是 <see cref="FileReleaseOutcome.StillReferenced"/>）。
/// 回收站的自动清除跑在没有当前用户的后台作用域里，走不了 <see cref="IFileStorageService.DeleteAsync"/>
/// 那条「这个人能不能删」的路，所以这里<b>不做授权判定：调用方负责确认这次删除是被允许的</b>。
/// </para>
/// <para>
/// 刻意不挂在 <see cref="IFileCleanupService"/> 上：清理服务以 <see cref="IStorageCleanupContributor"/> 驱动子模块的清理，
/// 子模块的回收站清除又要调这个操作 —— 放在一起，容器解析清理服务时就是一个环。
/// </para>
/// </remarks>
public interface IFileHoldService
{
    /// <summary>
    /// 放掉一个文件的<b>全部</b>隐式持有者；之后没有任何业务记录引用它，就连记录带对象删掉。
    /// </summary>
    /// <remarks>
    /// 先条件删掉记录、再删对象（本服务自己的工作单元提交之后；外层还开着工作单元时，它的提交只是一次 flush，对象仍在外层提交之前删掉，与 <c>DeleteAsync</c> 同一形状）；删不掉的交给 <c>FileDeleteRequestedEvent</c> 重试，记录删掉后发
    /// <c>FileRecordDeletedEvent</c>（工作区据此收掉版本、分享与回收站条目）。
    /// </remarks>
    Task<FileReleaseOutcome> ReleaseImplicitHoldsAsync(Guid fileId, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IFileHoldService.ReleaseImplicitHoldsAsync"/> 的结果。
/// </summary>
public enum FileReleaseOutcome
{
    /// <summary>没有这条记录。</summary>
    NotFound = 0,

    /// <summary>放掉之后无人持有，记录与对象已删除。</summary>
    Deleted = 1,

    /// <summary>仍被业务记录引用：记录与字节都留着，只是不再有隐式持有者。</summary>
    StillReferenced = 2
}
