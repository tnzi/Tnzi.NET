
namespace Tnzi.AI.Engine;

/// <summary>
/// 上下文注入接口
/// </summary>
/// <remarks>
/// <para>
/// 在 Agent 执行前注入额外上下文（RAG 检索结果、技能描述、历史记忆等），
/// 执行后可选地进行清理或存储操作。
/// </para>
/// </remarks>
public interface IContextProvider
{
    /// <summary>
    /// 提供器名称（默认为类型名）
    /// </summary>
    string Name => GetType().Name;

    /// <summary>
    /// 执行顺序（值越小越先执行，默认 0）
    /// </summary>
    int Order => 0;

    /// <summary>
    /// 是否在当前上下文中启用（默认始终启用）
    /// </summary>
    /// <param name="ctx">当前中间件上下文（可为 null）</param>
    bool IsEnabled(AiMiddlewareContext? ctx) => true;

    /// <summary>
    /// 在 Agent 执行前获取上下文注入
    /// </summary>
    /// <param name="messages">当前消息列表</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>上下文注入结果</returns>
    Task<ContextInjection> GetContextAsync(List<ChatMessage> messages, CancellationToken ct = default);

    /// <summary>
    /// 在 Agent 执行后回调（可选，用于保存新消息到向量存储等）
    /// </summary>
    /// <param name="messages">完整消息列表（含 Agent 回复）</param>
    /// <param name="ct">取消令牌</param>
    Task OnCompletedAsync(List<ChatMessage> messages, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// 上下文注入结果
/// </summary>
public class ContextInjection
{
    /// <summary>
    /// 要注入到消息列表前面的系统消息（如 RAG 上下文、技能描述）
    /// </summary>
    public List<ChatMessage>? Messages { get; set; }

    /// <summary>
    /// 要追加的工具（如按需技能工具）
    /// </summary>
    public List<AITool>? Tools { get; set; }

    /// <summary>
    /// RAG 引用来源（结构化 Citation 数据，用于透传给客户端）
    /// </summary>
    public List<CitationDto>? Citations { get; set; }

    /// <summary>
    /// 激活的技能列表（供 SkillConstraintMiddleware 消费）
    /// </summary>
    public List<SkillDefinition>? ActiveSkills { get; set; }

    /// <summary>
    /// 空注入
    /// </summary>
    public static readonly ContextInjection Empty = new();

    /// <summary>
    /// 是否有实际内容
    /// </summary>
    public bool HasContent => (Messages != null && Messages.Count > 0)
        || (Tools != null && Tools.Count > 0)
        || (ActiveSkills != null && ActiveSkills.Count > 0);
}
