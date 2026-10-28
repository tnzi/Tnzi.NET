namespace Tnzi.Signing.Services;

/// <summary>
/// 签署完成、PDF 压平密封之后，成品归档到哪里。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IMergeSourceProvider"/> 是同一条依赖倒置的两端：拥有宿主记录的业务模块
/// 实现本接口（把 PDF 挂成它自己的附件 / 案卷文档 / 人事档案），签署模块因此可以把结果交回去
/// 而<b>不需要认识</b>那些模块的任何实体。
/// </para>
/// <para>
/// ★ <b>实现必须幂等。</b>归档失败不回滚密封、只记日志；补救是管理端对这份已完成的请求再调一次
/// <c>IEnvelopeService.SealAsync</c>（<c>POST admin/signing/requests/{id}/seal</c>），它不重做成品，只把成品再交给
/// 本接口一次，而管理员并不知道上一次是不是其实已经挂上了（例如超时发生在写入之后）。框架不会自动重试。
/// "同一份合同在案卷里出现两次"这种事，发现它的通常不是我们，是客户。
/// </para>
/// <para>
/// ★★ <b>这里无从授权，也不该依赖当前用户。</b>归档发生在<b>最后一位签署人</b>的请求里：签署人通常不是系统用户，
/// 请求是匿名的（已切到信封所属的租户）；<c>entityId</c> 是发起人在创建时填的。绑定是否被允许，只能在创建那一刻、
/// 由同一宿主类型的 <see cref="IMergeSourceProvider.ResolveAsync"/> 按发起人的访问权判定（无权即抛
/// <c>ForbiddenException</c>，请求根本建不起来）。<b>给一个宿主类型注册了 sink，就要同时注册它的 provider</b>
/// （哪怕它没有任何合并变量、不覆写 <see cref="IMergeSourceProvider.Describe"/>）：只有 sink 没有 provider 的类型，
/// 绑定完全不经授权：任何持 <c>signing.request.create</c> 的人都能往任意一条该类记录里挂一份自己签完的 PDF。
/// 所以创建请求时遇到这种类型（点名了记录、只有 sink 没有 provider）一律失败关闭，请求建不起来。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "E-signature contracts are shaped by a single consumer so far; they may change before a second one validates them")]
public interface IDocumentHostSink
{
    /// <summary>本 sink 服务的宿主类型名。</summary>
    string EntityType { get; }

    /// <summary>把密封后的 PDF 挂到宿主记录上。</summary>
    /// <param name="entityId">宿主记录 id</param>
    /// <param name="fileId">密封 PDF 的 <c>Tnzi.Storage</c> 文件 id</param>
    /// <param name="fileName">展示用文件名</param>
    /// <param name="requestId">来源签署请求，供追溯</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task AttachAsync(
        Guid entityId,
        Guid fileId,
        string fileName,
        Guid requestId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 把容器里注册的 <see cref="IMergeSourceProvider"/> / <see cref="IDocumentHostSink"/> 按宿主类型归拢。
/// </summary>
/// <remarks>
/// 签署模块<b>从不点名</b>任何 provider 或 sink：它只按 <c>HostEntityType</c> 字符串来找。
/// 找不到不是错误 —— 一份不绑定任何宿主记录的独立文档完全合法（<c>HostEntityType</c> 为 null），
/// 而一个尚未接线的宿主类型只是暂时没有变量可合并、没有地方可归档。
/// </remarks>
[ExperimentalApi(Reason = "E-signature contracts are shaped by a single consumer so far; they may change before a second one validates them")]
public interface IMergeSourceRegistry
{
    /// <summary>按宿主类型取合并变量 provider；未注册返回 <c>null</c>。</summary>
    IMergeSourceProvider? FindProvider(string? entityType);

    /// <summary>按宿主类型取归档 sink；未注册返回 <c>null</c>。</summary>
    IDocumentHostSink? FindSink(string? entityType);

    /// <summary>已接线的全部宿主类型（provider 与 sink 的并集，不分大小写去重、排序）。</summary>
    /// <remarks>
    /// 预留给管理端的宿主类型选择（「哪些记录可以发起签署」）；<b>框架内目前没有调用方</b>，请求表单里的宿主类型仍是自由文本。
    /// </remarks>
    IReadOnlyList<string> KnownHostTypes { get; }
}
