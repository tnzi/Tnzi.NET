
namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// Agent 线程管理服务接口（公共 CRUD）
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
    /// 删除线程
    /// </summary>
    Task<Result> DeleteAsync(Guid id);
}

/// <summary>
/// Agent 线程内部服务接口（Pipeline 基础设施调用）
/// </summary>
public interface IAgentThreadInternalService
{
    /// <summary>
    /// 获取或创建线程对话上下文
    /// </summary>
    Task<ConversationContext> GetOrCreateThreadAsync(Guid? threadId, Guid agentId, CancellationToken ct = default);

    /// <summary>
    /// 保存消息到线程
    /// </summary>
    Task SaveMessageAsync(Guid threadId, string role, string content, string? toolCalls = null, string? usage = null, CancellationToken ct = default);

    /// <summary>
    /// 获取线程消息历史
    /// </summary>
    Task<List<ChatMessage>> GetMessageHistoryAsync(Guid threadId, int? limit = null, CancellationToken ct = default);

    /// <summary>
    /// 保存对话上下文的序列化数据到数据库
    /// </summary>
    Task SaveThreadSerializedDataAsync(Guid threadId, ConversationContext context, CancellationToken ct = default);
}
