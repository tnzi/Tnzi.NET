namespace Tnzi.Storage;

/// <summary>
/// 以<b>系统身份</b>读一份已存文件的字节。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IFileReadAccessProbe"/> 同一组契约、分工相反：那一个回答「<b>这个人</b>能不能读这份文件」，
/// 这一个只管「把这份文件读出来」，<b>不看</b>当前调用者是谁。两者合起来才是完整的一次引用：
/// 谁把一个文件 id 写进自己的记录，谁在那一刻过 <see cref="IFileReadAccessProbe"/>；
/// 之后按那条记录把字节取出来的，往往是没有任何当前用户的后台（队列里的邮件派发、定时任务、事件处理器），
/// 那里只能以系统身份读。
/// </para>
/// <para>
/// ★ <b>存在的理由是通知附件。</b>通知模块的 <c>Attachment.FileId</c> 从建表起就在，却从没被解析成字节：
/// 派发只转 <c>FilePath</c>，而 Storage 承载的产物的 <c>FilePath</c> 是存储相对键、URL 又要鉴权，
/// 内置邮件发送器两者都装不上 —— 于是发票邮件正文写着「请查收附件」，附件却从没发出去过。
/// 通知模块不引用 <c>Tnzi.Storage</c>，所以这个问题只能在核心问。
/// </para>
/// <para>
/// ★★ <b>刻意用 <c>Guid</c> 而不是存储的 <c>FileRecord</c> 实体</b>，与
/// <see cref="IFileReadAccessProbe"/> / <see cref="FileReferenceDescriptor"/> 同一条路子：
/// 拿着 id 的模块不必引用存储模块。
/// </para>
/// <para>
/// ★ <b>没有实现时不要静默放行。</b>本契约的实现随 <c>Tnzi.Storage</c> 一起注册；
/// 未加载存储模块的应用解析不到它，此时持有 <c>FileId</c> 的调用方应当<b>拒绝</b>并指名要加载的包，
/// 而不是把那个附件安静地丢掉 —— 后者与「发出去了」在接口上完全一致。
/// </para>
/// </remarks>
public interface IFileContentReader
{
    /// <summary>
    /// 打开 <paramref name="fileId"/> 指向的文件的只读流；文件不存在（或不在当前租户可见范围内）时返回
    /// <see langword="null"/>。调用方负责释放返回的流。
    /// </summary>
    Task<Stream?> OpenReadAsync(Guid fileId, CancellationToken cancellationToken = default);
}
