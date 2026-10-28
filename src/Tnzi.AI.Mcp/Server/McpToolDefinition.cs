namespace Tnzi.AI.Mcp.Server;

/// <summary>
/// 消费方自定义 MCP 工具：名字、描述、输入 schema 与 handler。经 <see cref="IMcpServerHost.ExposeTool(McpToolDefinition)"/> 注册。
/// </summary>
/// <remarks>
/// <para>
/// 宣告的形状就是 handler 收到的形状：<c>tools/list</c> 原样发出 <see cref="InputSchema"/>，
/// <c>tools/call</c> 的参数对象原样交给 <see cref="Handler"/>。
/// </para>
/// <para>
/// handler 的返回值原样发给客户端：有哪些内容块（文本、图片……）、<c>IsError</c> 是什么，都由 handler 决定；
/// 返回 <c>IsError=true</c> 与抛异常一样记为一次失败调用（审计与统计）。抛出的异常仍走统一守卫的映射：
/// <see cref="BusinessException"/> 透出它的消息，其余异常只给通用提示，两者都是 <c>IsError=true</c>。
/// </para>
/// <para>
/// 限流、审计 / 统计与运行范围凭据的工具白名单对它与对别的工具一样生效。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "The consumer tool contract returns ModelContextProtocol SDK result types and follows that SDK")]
public sealed class McpToolDefinition
{
    /// <summary>工具名：1-128 个字符，取自 A-Z、a-z、0-9、<c>_</c>、<c>-</c>、<c>.</c>（SDK 的规则），注册时校验。同名再注册即替换。</summary>
    public required string Name { get; init; }

    /// <summary>给模型看的工具说明。</summary>
    public required string Description { get; init; }

    /// <summary>
    /// 输入参数的 JSON Schema。顶层 <c>type</c> 必须是 <c>object</c>（注册时校验）；没有参数的工具给 <c>{"type":"object"}</c>。
    /// </summary>
    public required JsonElement InputSchema { get; init; }

    /// <summary>
    /// 执行工具。令牌在 MCP 请求被取消（客户端发来 <c>notifications/cancelled</c>）或客户端断开连接时触发，
    /// 长时间运行的工具应当把它一路传下去。
    /// </summary>
    public required Func<McpToolCallContext, CancellationToken, Task<CallToolResult>> Handler { get; init; }
}

/// <summary>
/// 一次自定义工具调用交给 handler 的上下文。
/// </summary>
[ExperimentalApi(Reason = "The consumer tool contract returns ModelContextProtocol SDK result types and follows that SDK")]
public sealed class McpToolCallContext
{
    /// <summary>被调用的工具名。</summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// <c>tools/call</c> 的参数对象，客户端发来什么就是什么；没带参数时为 <c>{}</c>。
    /// 框架<b>不</b>按 <see cref="McpToolDefinition.InputSchema"/> 校验它，必填项、类型与默认值由 handler 自己检查。
    /// </summary>
    public required JsonElement Arguments { get; init; }

    /// <summary>
    /// 本次调用专属的 DI 作用域，handler 返回后释放。仓储、DbContext 这类作用域服务从这里解析；
    /// 其中的 <see cref="ICurrentTenant"/> 已显式切到 <see cref="McpCallerScope.TenantId"/>（静态 API Key 切到宿主）。
    /// </summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>
    /// 调用方。静态 API Key 与进程内调用：<see cref="McpCallerScope.IsRunScoped"/> 为 false，运行相关的字段都是 null。
    /// 运行范围凭据（外部 CLI 运行回写）：运行、Agent、租户与派发这次运行的用户。
    /// </summary>
    public required McpCallerScope Caller { get; init; }
}
