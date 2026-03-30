namespace Tnzi.AI.Channels.Models;

/// <summary>
/// 入站消息类型
/// </summary>
public enum InboundMessageType
{
    /// <summary>普通聊天消息</summary>
    Chat,

    /// <summary>命令消息（如 /start, /help）</summary>
    Command
}
