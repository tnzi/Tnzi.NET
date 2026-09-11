using System.Text.RegularExpressions;

namespace Tnzi.Notification.Push.Services.Internal;

/// <summary>
/// FCM 主题名的字符集规则，以及不合法时该说什么。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么框架要自己判一遍，而不是让 SDK 抛。</b> <c>FirebaseAdmin</c> 在
/// <c>Message.CopyAndValidate()</c> 里确实会校验，但它抛的是
/// <c>ArgumentException("Malformed topic name.")</c> —— 既不说被拒的是哪个值，也不说什么才算合法。
/// 主题名是<b>外部输入</b>（消费方通常从配置里拼出来，例如 <c>alerts-{region}</c>），
/// 一条读不懂的失败原因会原样落进 <c>Recipient.FailureReason</c> 并出现在投递报告里，
/// 而运维手上除了那五个字什么都没有。
/// </para>
/// <para>
/// ★ <b>接受的集合与 SDK 对齐，包括 <c>/topics/</c> 前缀。</b> FCM 的官方文档里主题地址常写作
/// <c>/topics/news</c>，SDK 也接受这种写法（<c>Message.Topic</c> 的文档明说「may contain the
/// <c>/topics/</c> prefix」）。这里若只认裸名字，就会拒掉一个底层 API 本来接受的值 ——
/// 那是框架凭空造出来的不兼容，而症状是「照着 Firebase 文档写反而不行」。
/// <b>唯一一处比 SDK 严</b>是尾随换行，理由见 <see cref="TopicRegex"/>。
/// </para>
/// <para>
/// 规则本身是 FCM 的，所以它住在这个 FCM 程序集里而不是父模块：父模块的
/// <c>IPushSender</c> 契约不假定任何一家供应商，把某一家的字符集写进契约层，
/// 换一家实现时那条规则会以「框架规则」的身份继续拦人。
/// </para>
/// </remarks>
internal static partial class FcmTopicName
{
    /// <summary>FCM 文档规定的主题名字符集。</summary>
    internal const string Pattern = "[a-zA-Z0-9-_.~%]+";

    private const string TopicsPrefix = "/topics/";

    /// <remarks>
    /// ★ 锚点用 <c>\A…\z</c> 而不是 <c>^…$</c>。.NET 的 <c>$</c> 除了串尾，<b>也匹配串尾那个
    /// <c>\n</c> 之前的位置</b>，于是 <c>"news\n"</c> 能通过 <c>^[…]+$</c> ——
    /// 一个从配置或 CSV 里读出来、末尾带换行的主题名会被判成合法，推送发往一个
    /// 没有任何客户端订阅过的主题，而投递结果是成功的。<c>FirebaseAdmin</c> 自己那条
    /// 校验正是 <c>^…$</c>，所以这里比 SDK 严一点点是刻意的：尾随换行从来不是有意为之，
    /// 而它造成的失效毫无症状。
    /// </remarks>
    [GeneratedRegex(@"\A[a-zA-Z0-9-_.~%]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex TopicRegex();

    /// <summary>
    /// 这个值看着是不是一个<b>主题地址</b>被填进了设备令牌的位置。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>只认 <c>/topics/</c> 前缀，刻意不去猜裸名字。</b> FCM 注册令牌的字符集里没有斜杠，
    /// 所以以 <c>/topics/</c> 打头的值<b>必定</b>不是令牌 —— 这一条判得死，不会误伤。
    /// 而裸主题名（<c>news</c>）与一个短得离谱的令牌在字面上无从区分，去猜它就会开始
    /// 拒绝一些本该交给 FCM 去判的值，那比它想解决的问题更糟。
    /// </para>
    /// <para>
    /// 换来的是把一条<b>读不懂的远端错误</b>换成一条就地的指路：把 <c>/topics/news</c>
    /// 填进设备令牌（照着 Firebase 文档抄的人常这么干），FCM 回的是「不是合法的注册令牌」，
    /// 而真正的问题是<b>调错了方法</b>。
    /// </para>
    /// </remarks>
    /// <param name="deviceToken">调用方当作设备令牌传进来的值。</param>
    internal static bool LooksLikeTopicAddress(string? deviceToken)
        => deviceToken != null && deviceToken.StartsWith(TopicsPrefix, StringComparison.Ordinal);

    /// <summary>
    /// 判断主题名能不能投递；不能则给出一条<b>带上被拒值</b>的失败原因。
    /// </summary>
    /// <param name="topic">消费方给的主题名，允许带 <c>/topics/</c> 前缀。</param>
    /// <param name="failureReason">不合法时的失败原因，合法时为 <see langword="null"/>。</param>
    /// <returns>合法返回 <see langword="true"/>。</returns>
    internal static bool TryValidate(string? topic, out string? failureReason)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            failureReason = "Push topic name is required. "
                + $"A topic name must match {Pattern} (an optional /topics/ prefix is allowed).";
            return false;
        }

        var bare = topic.StartsWith(TopicsPrefix, StringComparison.Ordinal)
            ? topic[TopicsPrefix.Length..]
            : topic;

        if (!TopicRegex().IsMatch(bare))
        {
            // 引号是为了让首尾空白、不可见字符这类「肉眼看不出哪里错了」的值在日志里显形。
            failureReason = $"Push topic name '{topic}' is not a valid FCM topic. "
                + $"A topic name must match {Pattern} (an optional /topics/ prefix is allowed).";
            return false;
        }

        failureReason = null;
        return true;
    }
}
