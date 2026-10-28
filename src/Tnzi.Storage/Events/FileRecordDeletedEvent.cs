namespace Tnzi.Storage.Events;

/// <summary>
/// 一条 <see cref="Entities.FileRecord"/> 已从库里删除。
/// </summary>
/// <remarks>
/// <para>
/// 删除它的那条路径自己负责记录名下的两个对象（原件与缩略图，就地删或经 <see cref="FileDeleteRequestedEvent"/>）。
/// 本事件给的是<b>别的程序集</b>里挂在这个文件上的数据：例如工作区子模块的历史版本（每个版本一个自己的对象）
/// 与分享链接。父模块不认识那些表，只能广播；没有订阅者时什么都不发生。
/// </para>
/// <para>
/// 经事件总线的事务感知发布：删除所在的事务回滚，这个事件就不会发出。文件 id 不会被复用，
/// 处理器按 <see cref="FileId"/> 清理即可，但应先确认记录确实已不存在再动手，宁可少删，不能删掉仍在用的数据。
/// </para>
/// </remarks>
public class FileRecordDeletedEvent : EventBase
{
    /// <summary>
    /// 被删除的文件记录 id。
    /// </summary>
    public Guid FileId { get; set; }
}
