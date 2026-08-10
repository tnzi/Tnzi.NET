namespace Tnzi.Signing.Services;

/// <summary>
/// 一个合并变量的描述：模板设计器把它列出来供人选。
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
    /// 本 provider 能解析的全部变量，供模板设计器的下拉列表。
    /// </summary>
    /// <remarks>
    /// 必须<b>稳定且廉价</b>：它在每次打开设计器时被调用，而不是每份文档一次。
    /// 不要在这里查库。
    /// </remarks>
    IReadOnlyList<MergeFieldDescriptor> Describe();

    /// <summary>
    /// 解析一条宿主记录的实际取值，按 <see cref="MergeFieldDescriptor.Key"/> 归档。
    /// </summary>
    /// <remarks>
    /// ★ <b>解析不出来的键应当"不返回"，而不是返回空串。</b>两者对调用方是完全不同的事实：
    /// 前者是"这份记录没有这个信息"（可以据此提示合并不完整、让人先去补），后者是"这个值就是空的"。
    /// 混为一谈的后果是一份该拦下来的合同被安静地发出去，签名页上留着一处空白。
    /// </remarks>
    /// <param name="entityId">宿主记录 id</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IReadOnlyDictionary<string, object?>> ResolveAsync(Guid entityId, CancellationToken cancellationToken = default);
}
