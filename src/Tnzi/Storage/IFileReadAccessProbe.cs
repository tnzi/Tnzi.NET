namespace Tnzi.Storage;

/// <summary>
/// 反过来问一句：<b>当前调用者本人</b>能不能读这个文件。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IFileReferenceAccessResolver"/> 同一组契约、方向相反。那一个是业务模块
/// <b>告诉</b>存储「按我这条记录看，这个人可以读」；这一个是业务模块<b>问</b>存储
/// 「这个人本来就读得到这份文件吗」。
/// </para>
/// <para>
/// ★ <b>存在的理由是写入路径。</b>业务记录上的文件字段（<c>[FileField]</c>）通常来自
/// 请求体里的一个 id，而把一个 id 写进自己的记录，等于把那份文件<b>发布</b>给这条记录的
/// 全部可见者 —— 聊天消息、工单附件、审批单据都是这样。不问一句就写，任何人都能引用
/// 一个不属于自己的文件 id，然后经由自己那条记录的可见性规则去读它。
/// </para>
/// <para>
/// ★★ <b>刻意用 <c>Guid</c> 而不是存储的 <c>FileRecord</c> 实体</b>：拥有业务记录的模块
/// （Chat / Finance / 消费应用）要能在<b>不引用 <c>Tnzi.Storage</c></b> 的前提下问这个问题，
/// 与 <see cref="FileReferenceDescriptor"/> 刻意只用原始类型是同一条路子。
/// </para>
/// <para>
/// ★★ <b>它问的是「这个人」，不是「这一次请求」。</b>实现必须<b>不认</b>请求级凭据
/// （URL 签名令牌、分享链接授予）：那些证明的是「这一次渲染请求被允许」。认了的话，
/// 一条限次数、会过期的分享链接就能被换成一条永久引用 —— 引用一旦写进业务记录，
/// 它的可见性就由那条记录说了算，与原来那份凭据的约束再无关系。
/// </para>
/// <para>
/// ★ <b>没有实现时不要静默放行。</b>本契约的实现随 <c>Tnzi.Storage</c> 一起注册；
/// 未加载存储模块的应用解析不到它，此时调用方应当<b>拒绝</b>带文件的写入并指名要加载的包，
/// 而不是把校验跳过去 —— 后者与「校验通过」在接口上完全一致。
/// </para>
/// </remarks>
public interface IFileReadAccessProbe
{
    /// <summary>
    /// 当前调用者本人能否读取 <paramref name="fileId"/> 指向的文件。
    /// 文件不存在时返回 <see langword="false"/>（引用一个不存在的 id 不是一次合法的写入）。
    /// </summary>
    Task<bool> CanReadAsync(Guid fileId, CancellationToken cancellationToken = default);
}
