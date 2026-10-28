
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
    /// 归档或取消归档线程。归档已归档的线程保留原归档时间（幂等）。
    /// </summary>
    Task<Result<AgentThreadDto>> SetArchivedAsync(Guid id, bool archived)
        => Task.FromResult(Result<AgentThreadDto>.Failure(
            "Archiving threads is not supported by this thread service.", 501, ErrorCodes.InternalError));

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

    /// <summary>
    /// 往回翻页取线程消息：<paramref name="beforeOrder"/> 之前（不含）的最近 <paramref name="limit"/> 条，旧的在前；
    /// <paramref name="beforeOrder"/> 为 null 时取最新的一页。调用方负责先核线程归属。
    /// </summary>
    Task<Result<ThreadMessagePageDto>> GetMessagesAsync(Guid id, int? beforeOrder, int limit)
        => Task.FromResult(Result<ThreadMessagePageDto>.Failure(
            "Paging thread messages is not supported by this thread service.", 501, ErrorCodes.InternalError));
}

/// <summary>
/// Agent 线程内部服务接口（AgentRuntime 调用）
/// </summary>
public interface IAgentThreadInternalService
{
    /// <summary>
    /// 解析或创建本轮所属的线程，返回实际使用的 ThreadId 及是否为新建线程的标志。
    /// 给定的线程不存在、不归运行发起人所有、或与 <paramref name="agentId"/> 不符时一律抛 404 的 <see cref="BusinessException"/>。
    /// 历史消息不在这里加载（见 <see cref="GetMessageHistoryAsync"/>）。
    /// </summary>
    Task<(Guid threadId, bool isNewThread)> GetOrCreateThreadAsync(Guid? threadId, Guid? agentId, CancellationToken ct = default);

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
    /// 保存一条用户消息，连同客户端随这一轮带来的每轮元数据（<c>ChatRequestDto.Metadata</c>）。
    /// </summary>
    /// <remarks>
    /// 框架的实现把元数据存在同一行里（<c>AgentThreadMessage.MetadataJson</c>）。默认实现只存消息、不存元数据，
    /// 给替换了本接口的实现保持编译；那样的实现上，重新生成拿不回原来那一轮的元数据。
    /// </remarks>
    Task<Guid> SaveUserMessageAsync(Guid threadId, string content, IReadOnlyDictionary<string, JsonElement>? metadata, Guid? messageId = null, CancellationToken ct = default)
        => SaveMessageAsync(threadId, MessageRole.User, content, messageId: messageId, ct: ct);

    /// <summary>
    /// 获取线程消息历史
    /// </summary>
    Task<List<ChatMessage>> GetMessageHistoryAsync(Guid threadId, int? limit = null, CancellationToken ct = default);

    /// <summary>
    /// 读取线程元数据（<c>AgentThread.Metadata</c> JSON 对象）中某个键的值，返回该值的原始 JSON 文本；
    /// 线程不存在或键不存在返回 null。框架内部按键存放跨轮次的运行状态（如已激活技能）。
    /// </summary>
    Task<string?> GetMetadataValueAsync(Guid threadId, string key, CancellationToken ct = default);

    /// <summary>
    /// 找线程里的一条用户消息：<paramref name="messageId"/> 给出时按 id（必须是这条线程的用户消息），
    /// 为 null 时取最后一条用户消息。找不到返回 null。
    /// </summary>
    Task<ThreadMessageRef?> FindUserMessageAsync(Guid threadId, Guid? messageId, CancellationToken ct = default)
        => throw new NotSupportedException("This thread service cannot look up messages for editing.");

    /// <summary>
    /// 回退线程：删掉 <paramref name="fromOrder"/> 及其后的全部消息（连同它们的附件），返回删掉的条数。
    /// 编辑与重新生成用它；没有实现时调用方失败而不是假装成功。
    /// </summary>
    Task<int> DeleteMessagesFromAsync(Guid threadId, int fromOrder, CancellationToken ct = default)
        => throw new NotSupportedException("This thread service cannot rewind a thread.");

    /// <summary>
    /// 写入线程元数据中某个键的值（<paramref name="valueJson"/> 为该值的 JSON 文本；null = 移除该键）。
    /// 其它键原样保留。线程不存在时不做任何事。
    /// </summary>
    Task SetMetadataValueAsync(Guid threadId, string key, string? valueJson, CancellationToken ct = default);
}
