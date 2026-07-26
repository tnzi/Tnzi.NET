namespace Tnzi.AI.Channels.Gateway.Models;

/// <summary>
/// 会话绑定解析结果 - DefaultSessionBinder 的输出
/// </summary>
public class SessionBinding
{
    /// <summary>绑定的 Agent ID</summary>
    public Guid AgentId { get; init; }

    /// <summary>会话作用域</summary>
    public SessionScope Scope { get; init; }

    /// <summary>会话唯一键（由 Scope 规则生成）</summary>
    public string SessionKey { get; init; } = string.Empty;

    /// <summary>关联的线程 ID（可选，已存在的会话才有）</summary>
    public Guid? ThreadId { get; init; }
}
