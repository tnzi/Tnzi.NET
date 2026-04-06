namespace Tnzi.AI.Channels.Gateway.Models;

/// <summary>
/// 会话作用域 — 决定 Gateway 如何映射入站请求到 Agent 线程
/// </summary>
public enum SessionScope
{
    /// <summary>全局唯一会话（所有用户共享同一线程）</summary>
    Global,

    /// <summary>每个 Peer（用户）独立会话</summary>
    PerPeer,

    /// <summary>每个频道+Peer 组合独立会话</summary>
    PerChannelPeer,

    /// <summary>每个 Thread（话题）独立会话</summary>
    PerThread
}
