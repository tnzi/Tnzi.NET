namespace Tnzi.AI.Channels.Models;

/// <summary>
/// 发送到 IM 平台的出站消息
/// </summary>
/// <param name="ChannelName">适配器名称（telegram / slack / ...）</param>
/// <param name="ChatId">平台聊天 / 频道 ID</param>
/// <param name="ThreadId">Agent 线程 ID（无线程时为 Guid.Empty）</param>
/// <param name="Text">回复文本</param>
/// <param name="ArtifactPaths">产物路径（保留字段，当前无生产者）</param>
/// <param name="IsFinal">是否为最终消息（流式场景下的终止块）</param>
/// <param name="ThreadTs">平台线程指针（Slack thread_ts / Discord 消息 id），从入站消息原样带回，回复落在同一线程</param>
/// <param name="TopicId">平台话题 id（Telegram 论坛话题 message_thread_id），从入站消息原样带回，回复落在同一话题</param>
/// <param name="Metadata">入站附带的平台元数据（如 Discord 交互令牌），适配器据此选择回复通道</param>
public record OutboundMessage(
    string ChannelName,
    string ChatId,
    Guid ThreadId,
    string Text,
    List<string>? ArtifactPaths = null,
    bool IsFinal = true,
    string? ThreadTs = null,
    Dictionary<string, object>? Metadata = null,
    string? TopicId = null);
