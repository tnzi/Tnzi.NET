namespace Tnzi.AI.Channels.Adapters.Dingtalk;

/// <summary>
/// 钉钉回调随入站消息携带的元数据键：回复该发往哪条接口、发给谁。
/// </summary>
/// <remarks>
/// 钉钉机器人的单聊与群聊是两条发送接口，收件人的标识也不同：单聊
/// <c>POST /v1.0/robot/oToMessages/batchSend</c> 要员工的 <c>staffId</c>（<c>userIds</c>），
/// 群聊 <c>POST /v1.0/robot/groupMessages/send</c> 要会话的 <c>openConversationId</c>。
/// 入站消息的 <c>ChatId</c> 是 <c>conversationId</c>（线程映射的键），它不是任何员工的 id：
/// 拿它当 <c>userIds</c> 发，钉钉照样回 200，只是把它列进 <c>invalidStaffIdList</c>、什么都不投递。
/// 这两个值经 <see cref="Models.InboundMessage.Metadata"/> 穿过 ChannelManager 回到
/// <see cref="Models.OutboundMessage.Metadata"/>，适配器据此选择接口与收件人。
/// </remarks>
public static class DingtalkConversationMetadata
{
    /// <summary>会话类型（回调里的 <c>conversationType</c>）：<c>"1"</c> 单聊，<c>"2"</c> 群聊。</summary>
    public const string ConversationType = "dingtalk.conversationType";

    /// <summary>发送者的员工 id（回调里的 <c>senderStaffId</c>），单聊回复的收件人。</summary>
    public const string SenderStaffId = "dingtalk.senderStaffId";

    /// <summary>单聊。</summary>
    public const string SingleChat = "1";

    /// <summary>群聊。</summary>
    public const string GroupChat = "2";
}
