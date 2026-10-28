namespace Tnzi.AI.Channels.Models;

/// <summary>
/// 会话类型的取值，绑定规则（<c>SessionBindingRule.PeerKind</c> / <c>AI:Channels:Gateway:BindingRules[].PeerKind</c>）按它匹配。
/// </summary>
/// <remarks>
/// 各平台的来源：Telegram <c>chat.type</c>（private 为单聊，group / supergroup / channel 为群聊）、
/// Slack <c>channel_type</c>（im 为单聊，channel / group / mpim 为群聊）、钉钉 <c>conversationType</c>（1 / 2）、
/// 飞书 <c>chat_type</c>（p2p / group）、Discord 有无 <c>guild_id</c>（无为私信）。WebSocket 连接一律是单聊。
/// 平台没给出类型时为 null，此时只有不限定 PeerKind 的规则会命中。
/// </remarks>
public static class ChannelPeerKinds
{
    /// <summary>与机器人一对一的单聊（私信）。</summary>
    public const string User = "user";

    /// <summary>群聊、频道或多人会话。</summary>
    public const string Group = "group";
}
