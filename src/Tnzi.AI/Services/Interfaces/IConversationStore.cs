namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 对话存储抽象 — 解耦对话持久化方式
/// </summary>
/// <remarks>
/// <para>
/// 内置两种实现：DatabaseConversationStore（桥接 AgentThread 实体，默认）
/// 和 FileConversationStore（文件系统存储，适用于 CLI 场景无需数据库）。
/// </para>
/// <para>
/// conversationId 是不透明字符串标识符，调用方不应假设其格式。
/// 各实现内部自行处理标识符的格式转换和验证。
/// </para>
/// </remarks>
public interface IConversationStore
{
    /// <summary>
    /// 获取或创建对话上下文
    /// </summary>
    /// <param name="conversationId">对话 ID（不透明字符串标识符）</param>
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
