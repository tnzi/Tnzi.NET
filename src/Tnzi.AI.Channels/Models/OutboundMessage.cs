namespace Tnzi.AI.Channels.Models;

/// <summary>
/// 发送到 IM 平台的出站消息
/// </summary>
public record OutboundMessage(
    string ChannelName,
    string ChatId,
    Guid ThreadId,
    string Text,
    List<string>? ArtifactPaths = null,
    bool IsFinal = true,
    string? ThreadTs = null,
    Dictionary<string, object>? Metadata = null);
