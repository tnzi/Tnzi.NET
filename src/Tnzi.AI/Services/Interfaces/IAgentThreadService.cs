
namespace Tnzi.AI.Services;

/// <summary>
/// Agent 线程管理服务接口（公共 CRUD + 查询）
/// </summary>
public interface IAgentThreadService
{
    /// <summary>
    /// 创建线程
    /// </summary>
    Task<Result<AgentThreadDto>> CreateAsync(CreateAgentThreadDto input);

    /// <summary>
    /// 根据 ID 获取线程
    /// </summary>
    Task<Result<AgentThreadDto>> GetByIdAsync(Guid id);

    /// <summary>
    /// 获取线程详情（含最近消息）
    /// </summary>
    Task<Result<AgentThreadDetailDto>> GetDetailAsync(Guid id, int messageLimit = 50);

    /// <summary>
    /// 获取线程列表（分页 + 筛选）
    /// </summary>
    Task<Result<IPagedList<AgentThreadDto>>> GetListAsync(ThreadListQueryDto query);

    /// <summary>
    /// 更新线程标题
    /// </summary>
    Task<Result<AgentThreadDto>> UpdateTitleAsync(Guid id, string title);

    /// <summary>
    /// 验证用户是否为线程所有者
    /// </summary>
    Task<bool> IsOwnerAsync(Guid threadId, Guid userId);

    /// <summary>
    /// 删除线程
    /// </summary>
    Task<Result> DeleteAsync(Guid id);

    /// <summary>
    /// 导出线程为 JSON 格式
    /// </summary>
    Task<Result<ThreadExportDto>> ExportAsJsonAsync(Guid id);

    /// <summary>
    /// 导出线程为 Markdown 格式
    /// </summary>
    Task<Result<string>> ExportAsMarkdownAsync(Guid id);
}

/// <summary>
/// Agent 线程内部服务接口（AgentRuntime 调用）
/// </summary>
public interface IAgentThreadInternalService
{
    /// <summary>
    /// 获取或创建线程对话上下文，返回上下文、实际使用的 ThreadId 及是否为新建线程的标志
    /// </summary>
    Task<(ConversationContext context, Guid threadId, bool isNewThread)> GetOrCreateThreadAsync(Guid? threadId, Guid? agentId, CancellationToken ct = default);

    /// <summary>
    /// 保存消息到线程。
    /// </summary>
    /// <param name="threadId">目标线程 ID</param>
    /// <param name="role">消息角色（user / assistant / tool …）</param>
    /// <param name="content">消息正文</param>
    /// <param name="toolCalls">工具调用的序列化 JSON；无则为 null</param>
    /// <param name="usage">Token 用量的序列化 JSON；无则为 null</param>
    /// <param name="messageId">
    /// Optional pre-generated message ID. When supplied, the message is persisted with this
    /// exact ID - letting callers (e.g. streaming pipelines) surface the ID to clients
    /// before the database write completes. When null, the framework generates one.
    /// </param>
    /// <param name="ct">取消令牌</param>
    /// <returns>The persisted message ID.</returns>
    Task<Guid> SaveMessageAsync(Guid threadId, string role, string content, string? toolCalls = null, string? usage = null, Guid? messageId = null, CancellationToken ct = default);

    /// <summary>
    /// 获取线程消息历史
    /// </summary>
    Task<List<ChatMessage>> GetMessageHistoryAsync(Guid threadId, int? limit = null, CancellationToken ct = default);

    /// <summary>
    /// 保存对话上下文的序列化数据到数据库
    /// </summary>
    Task SaveThreadSerializedDataAsync(Guid threadId, ConversationContext context, CancellationToken ct = default);

    /// <summary>
    /// 读取线程元数据（<c>AgentThread.Metadata</c> JSON 对象）中某个键的值，返回该值的原始 JSON 文本；
    /// 线程不存在或键不存在返回 null。框架内部按键存放跨轮次的运行状态（如已激活技能）。
    /// </summary>
    Task<string?> GetMetadataValueAsync(Guid threadId, string key, CancellationToken ct = default);

    /// <summary>
    /// 写入线程元数据中某个键的值（<paramref name="valueJson"/> 为该值的 JSON 文本；null = 移除该键）。
    /// 其它键原样保留。线程不存在时不做任何事。
    /// </summary>
    Task SetMetadataValueAsync(Guid threadId, string key, string? valueJson, CancellationToken ct = default);
}
