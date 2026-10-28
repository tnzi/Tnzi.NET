namespace Tnzi.Signing.Services;

/// <summary>
/// 一个合并变量的描述：预留给计划中的合并变量选择器（见 <see cref="IMergeSourceProvider.Describe"/>）。
/// </summary>
/// <param name="Key">变量键（模板里写 <c>{{Key}}</c>）</param>
/// <param name="Label">给人看的名字</param>
/// <param name="Group">分组（设计器里的下拉分节），可空</param>
/// <param name="Sample">示例值，让人在插入前知道它长什么样</param>
public sealed record MergeFieldDescriptor(string Key, string Label, string? Group = null, string? Sample = null);

/// <summary>
/// 声明某一类宿主记录能提供哪些合并变量，并负责解析它们的实际值。
/// </summary>
/// <remarks>
/// <para>
/// <b>依赖方向是本设计的全部要点。</b>签署模块<b>不认识</b>任何业务模块 —— 一份文档通过
/// <c>HostEntityType</c> + <c>HostEntityId</c> 多态绑定到它的宿主记录。拥有那类记录的业务模块
/// 实现本接口并注册进容器；签署模块只按名字找 provider，永远不引用它们。
/// </para>
/// <para>
/// 这取代的是"在服务里写死一串 <c>.Replace("{{ClientName}}", ...)</c>"—— 那种写法每加一个变量
/// 就要改一次引擎，而且引擎必须认识每一种业务对象。现在扩充词汇表 = 多返回一个
/// <see cref="MergeFieldDescriptor"/>。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "E-signature contracts are shaped by a single consumer so far; they may change before a second one validates them")]
public interface IMergeSourceProvider
{
    /// <summary>本 provider 服务的宿主类型名（与 <c>Envelope.HostEntityType</c> 对应）。</summary>
    string EntityType { get; }

    /// <summary>
    /// 本 provider 能解析的全部变量，预留给计划中的合并变量选择器（模板设计器的下拉列表）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>框架内目前没有调用方</b>：没有端点暴露它，管理端字段编辑器里的「绑定」与请求表单里的宿主类型都还是自由文本，
    /// 键拼错（<c>Client.Name</c> 对 <c>ClientName</c>）只会表现为一个解析不出的变量（必填的在发出时被拦下，不必填的留白）。
    /// 所以给了默认实现（空列表），实现方可以不覆写；选择器落地后再来填它。
    /// </para>
    /// <para>
    /// 要填的话，必须<b>稳定且廉价</b>：选择器每次打开都会调用它，而不是每份文档一次。不要在这里查库。
    /// </para>
    /// </remarks>
    IReadOnlyList<MergeFieldDescriptor> Describe() => [];

    /// <summary>
    /// 解析一条宿主记录的实际取值，按 <see cref="MergeFieldDescriptor.Key"/> 归档。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★ <b>授权由实现负责，这里就是宿主绑定唯一的授权点。</b>本方法在<b>发起人</b>的请求里执行
    /// （创建签署请求时、任何落库与排版之前），<paramref name="entityId"/> 是发起人自己填的：
    /// 签署模块只要求 <c>signing.request.create</c>，它不认识宿主记录，无从判断这个人能不能看这一条。
    /// 不判就意味着：一个被行级授权挡在某条记录之外的人，拿一份正文写满 <c>{{变量}}</c> 的 Composed 模板发起一次，
    /// 就能从渲染出的 PDF 里读到那条记录的字段；签完之后 <see cref="IDocumentHostSink.AttachAsync"/>
    /// 还会把这份他自己签的 PDF 挂进那条记录。
    /// </para>
    /// <para>
    /// 所以实现必须按<b>当前用户</b>对这条记录的访问权判定（数据权限、归属、案卷成员……用业务模块自己的规则），
    /// 无权时<b>抛 <c>ForbiddenException</c></b>（创建请求答 403，什么都不落库），不要返回空字典：
    /// 空字典只是「没有可合并的信息」，请求照样建起来、照样绑在那条记录上，完成时照样归档进去。
    /// 不存在的记录同理按无权处理，不要借 404 / 403 的差别泄露存在性。
    /// </para>
    /// <para>
    /// ★ <b>解析不出来的键应当"不返回"，而不是返回空串。</b>两者对调用方是完全不同的事实：
    /// 前者是"这份记录没有这个信息"（可以据此提示合并不完整、让人先去补），后者是"这个值就是空的"。
    /// 混为一谈的后果是一份该拦下来的合同被安静地发出去，签名页上留着一处空白。
    /// </para>
    /// </remarks>
    /// <param name="entityId">宿主记录 id</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IReadOnlyDictionary<string, object?>> ResolveAsync(Guid entityId, CancellationToken cancellationToken = default);
}
