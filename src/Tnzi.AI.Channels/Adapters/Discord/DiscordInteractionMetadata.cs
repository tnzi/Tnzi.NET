namespace Tnzi.AI.Channels.Adapters.Discord;

/// <summary>
/// Discord 交互（Interaction）随入站消息携带的元数据键。
/// 斜杠命令经签过名的 HTTP 回调进来，三秒内只能先答「延迟应答」，真正的回复要凭交互令牌
/// 经 <c>PATCH /webhooks/{application_id}/{token}/messages/@original</c> 回写（令牌 15 分钟有效）；
/// <see cref="Models.InboundMessage.Metadata"/> 带着这两个值穿过 ChannelManager 回到
/// <see cref="Models.OutboundMessage.Metadata"/>，适配器据此选择回复通道。
/// </summary>
public static class DiscordInteractionMetadata
{
    /// <summary>交互令牌（Interaction.token）</summary>
    public const string Token = "discord.interactionToken";

    /// <summary>应用 ID（Interaction.application_id）</summary>
    public const string ApplicationId = "discord.applicationId";
}
