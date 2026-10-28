namespace Tnzi.AI.Cli.Adapters;

/// <summary>
/// 协议适配器。<b>一个实现对应一个协议族，覆盖所有说该协议的 provider。</b>
/// </summary>
/// <remarks>
/// 这是本模块的核心结构选择：把「协议」（代码）与「provider 参数」（数据，见
/// <see cref="CliProviderDescriptor"/>）分开。于是 ACP 适配器一次写完就覆盖 6 个 CLI，
/// 而不是每个 CLI 一份近万行的后端实现。
/// <para>
/// 适配器是<b>有状态且一次性</b>的：一个实例驱动一次会话。<see cref="GetResult"/>
/// 必须在 <see cref="RunAsync"/> 迭代完成之后调用。
/// </para>
/// </remarks>
public interface ICliProtocolAdapter
{
    /// <summary>本适配器负责的协议族。</summary>
    CliAgentProtocol Protocol { get; }

    /// <summary>构造进程启动参数。</summary>
    CliProcessSpec BuildProcess(CliAgentLaunchContext context);

    /// <summary>
    /// 驱动一次完整会话，流式产出归一化事件。
    /// </summary>
    /// <remarks>
    /// <paramref name="transport"/> 是<b>双向</b>的：stream-json 需要保持 stdin 打开以应答
    /// <c>control_request</c>，ACP 需要完整的 JSON-RPC 收发（含 agent 反向发起的
    /// <c>session/request_permission</c>）。单向读契约装不下这两者。
    /// </remarks>
    IAsyncEnumerable<CliAgentEvent> RunAsync(
        ICliAgentTransport transport,
        CliAgentLaunchContext context,
        CancellationToken cancellationToken);

    /// <summary>会话结束后取终态。</summary>
    CliAgentResult GetResult(CliSessionOutcome outcome);

    /// <summary>
    /// 把使用方对一个权限请求的回答写给 CLI（<see cref="CliAgentLaunchContext.AsksForApproval"/> 时，适配器产出的
    /// <see cref="CliAgentEventType.PermissionRequest"/> 事件带的 <see cref="CliAgentPermission.ProtocolKey"/>）。
    /// </summary>
    /// <remarks>
    /// <para>可以在 <see cref="RunAsync"/> 迭代期间从别的线程调用。写入在返回之前已经交给 transport（重放时按调用次序计 stdin 行）。</para>
    /// <para>默认实现返回 false：不转发权限请求的适配器从不产出 <c>PermissionRequest</c> 事件。</para>
    /// </remarks>
    /// <returns>认得这个键、写出了应答为 true；键不认识或已经应答过为 false。</returns>
    Task<bool> AnswerPermissionAsync(string protocolKey, CliPermissionDecision decision, CancellationToken cancellationToken)
        => Task.FromResult(false);
}

/// <summary>
/// 交给 CLI 的一个权限回答。
/// </summary>
/// <param name="Allow">允许还是拒绝。</param>
/// <param name="OptionId">选中的选项 id（请求给出的那些之一）；超时、随运行结束时为 null，适配器自己挑一个单次拒绝。</param>
/// <param name="Message">拒绝时交给 agent 的说明。</param>
public sealed record CliPermissionDecision(bool Allow, string? OptionId, string? Message);

/// <summary>
/// 按协议族解析适配器实例。
/// </summary>
public interface ICliProtocolAdapterFactory
{
    /// <summary>
    /// 为一次会话创建适配器。协议无实现时抛
    /// <see cref="CliProtocolNotImplementedException"/> —— 描述表里存在不等于可用。
    /// </summary>
    ICliProtocolAdapter Create(CliAgentProtocol protocol);

    /// <summary>该协议在本版本是否已有实现。</summary>
    bool IsImplemented(CliAgentProtocol protocol);
}
