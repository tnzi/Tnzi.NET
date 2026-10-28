namespace Tnzi.AI.Channels.Models;

/// <summary>
/// 从 IM 平台接收到的入站消息
/// </summary>
/// <param name="ChannelName">适配器名称（telegram / slack / ...）</param>
/// <param name="ChatId">平台聊天 / 频道 ID</param>
/// <param name="UserId">发送者 ID</param>
/// <param name="Text">消息文本</param>
/// <param name="Type">聊天或命令</param>
/// <param name="ThreadTs">平台线程指针（Slack thread_ts / Discord 消息 id 等），回复原样带回</param>
/// <param name="TopicId">平台话题 id（Telegram 论坛话题），参与线程映射的键</param>
/// <param name="Metadata">平台元数据，原样带到回复上（适配器据此选择回复通道）</param>
/// <param name="PeerKind">
/// 会话类型（<see cref="ChannelPeerKinds"/>）：单聊 <c>"user"</c>、群聊 / 频道 <c>"group"</c>，平台没说时为 null。
/// 绑定规则的 <c>PeerKind</c> 按它匹配；没有这个值，一条带 PeerKind 的规则永远不会命中。
/// </param>
public record InboundMessage(
    string ChannelName,
    string ChatId,
    string UserId,
    string Text,
    InboundMessageType Type = InboundMessageType.Chat,
    string? ThreadTs = null,
    string? TopicId = null,
    Dictionary<string, object>? Metadata = null,
    string? PeerKind = null);
