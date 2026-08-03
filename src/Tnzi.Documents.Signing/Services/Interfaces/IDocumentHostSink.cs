namespace Tnzi.Documents.Signing.Services;

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
/// ★ <b>实现必须幂等。</b>完成动作会在瞬时失败后被重试（密封成功但归档那一步超时是最典型的情形），
/// 而"同一份合同在案卷里出现两次"这种事，发现它的通常不是我们，是客户。
/// </para>
/// </remarks>
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
public interface IMergeSourceRegistry
{
    /// <summary>按宿主类型取合并变量 provider；未注册返回 <c>null</c>。</summary>
    IMergeSourceProvider? FindProvider(string? entityType);

    /// <summary>按宿主类型取归档 sink；未注册返回 <c>null</c>。</summary>
    IDocumentHostSink? FindSink(string? entityType);

    /// <summary>已接线的全部宿主类型（供管理端展示"哪些记录可以发起签署"）。</summary>
    IReadOnlyList<string> KnownHostTypes { get; }
}
