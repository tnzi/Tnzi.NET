namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 对话存储抽象 — 解耦对话持久化方式
/// </summary>
/// <remarks>
/// 默认实现为 DatabaseConversationStore（桥接 IAgentThreadInternalService）。
/// Tnzi.AI.Coder 模块提供 FileConversationStore 替代实现（CLI 场景不需要数据库）。
/// </remarks>
public interface IConversationStore
{
    /// <summary>
    /// 获取或创建对话上下文
    /// </summary>
    /// <param name="conversationId">对话 ID。数据库实现（DatabaseConversationStore）要求 GUID 格式字符串；
    /// 文件实现（FileConversationStore）接受任意合法文件名字符串。</param>
    /// <param name="agentId">关联的 Agent ID（可选）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>对话上下文</returns>
    Task<ConversationContext> GetOrCreateAsync(string conversationId, Guid? agentId = null, CancellationToken ct = default);

    /// <summary>
    /// 保存对话上下文
    /// </summary>
    /// <param name="conversationId">对话 ID</param>
    /// <param name="context">对话上下文</param>
    /// <param name="ct">取消令牌</param>
    Task SaveAsync(string conversationId, ConversationContext context, CancellationToken ct = default);

    /// <summary>
    /// 追加消息到对话
    /// </summary>
    /// <param name="conversationId">对话 ID</param>
    /// <param name="role">消息角色</param>
    /// <param name="content">消息内容</param>
    /// <param name="ct">取消令牌</param>
    Task AppendMessageAsync(string conversationId, string role, string content, CancellationToken ct = default);

    /// <summary>
    /// 列出对话摘要
    /// </summary>
    /// <param name="limit">最大返回数量</param>
    /// <param name="ct">取消令牌</param>
    Task<IReadOnlyList<ConversationSummary>> ListAsync(int limit = 50, CancellationToken ct = default);

    /// <summary>
    /// 删除对话
    /// </summary>
    /// <param name="conversationId">对话 ID</param>
    /// <param name="ct">取消令牌</param>
    Task DeleteAsync(string conversationId, CancellationToken ct = default);
}

/// <summary>
/// 对话摘要信息
/// </summary>
public class ConversationSummary
{
    /// <summary>
    /// 对话 ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 对话标题
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActivity { get; set; }

    /// <summary>
    /// 消息数量
    /// </summary>
    public int MessageCount { get; set; }
}
