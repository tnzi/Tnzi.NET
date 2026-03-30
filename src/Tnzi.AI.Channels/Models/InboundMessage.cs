namespace Tnzi.AI.Channels.Models;

/// <summary>
/// 从 IM 平台接收到的入站消息
/// </summary>
public record InboundMessage(
    string ChannelName,
    string ChatId,
    string UserId,
    string Text,
    InboundMessageType Type = InboundMessageType.Chat,
    string? ThreadTs = null,
    string? TopicId = null,
    List<FileAttachmentInfo>? Files = null,
    Dictionary<string, object>? Metadata = null);
