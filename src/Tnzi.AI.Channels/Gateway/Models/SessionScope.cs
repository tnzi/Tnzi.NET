namespace Tnzi.AI.Channels.Gateway.Models;

/// <summary>
/// 会话作用域 - 决定活跃会话列表（<c>IGateway.GetSessionsAsync</c> / WebSocket <c>session.list</c>）里
/// 哪些对话归为同一条会话记录，即会话键（<see cref="SessionBinding.SessionKey"/>）怎么拼。
/// </summary>
/// <remarks>
/// <b>它不决定消息跑在哪条 Agent 线程上。</b>线程始终按（渠道, 聊天, 话题）映射：同一个聊天 / 话题的消息接在同一条线程上，
/// 不同聊天各有各的线程，与作用域无关。一条记录只保留最近一次活动（Agent、线程、发送者）。
/// </remarks>
public enum SessionScope
{
    /// <summary>该 Agent 的全部对话归为一条会话记录</summary>
    Global,

    /// <summary>按发送者归为会话记录（同一用户在不同渠道的对话是同一条记录）</summary>
    PerPeer,

    /// <summary>按渠道 + 发送者归为会话记录</summary>
    PerChannelPeer,

    /// <summary>按渠道 + 聊天归为会话记录</summary>
    PerThread
}
