using FirebaseAdmin.Messaging;
// 两个别名是消歧：在 Tnzi.Notification.* 命名空间里，裸的 Notification 先解析成命名空间 Tnzi.Notification。
using FcmMessage = FirebaseAdmin.Messaging.Message;
using FcmNotification = FirebaseAdmin.Messaging.Notification;

namespace Tnzi.Notification.Push.Services.Internal;

/// <summary>
/// 把一条 <see cref="PushContent"/> 变成 FCM 的消息，以及其中哪些值 FCM 收不下、该怎么说。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>点击目标进 <c>webpush.fcm_options.link</c>，数据进顶层 <c>data</c>。</b>前者只有 web 推送认得
/// （浏览器里点开通知时打开它），后者发给所有平台。刻意<b>不把链接顺手抄进数据</b>：那需要一个框架保留的键，
/// 而它会与消费方自己的键相撞；原生客户端要路由，消费方把它要的东西放进 <see cref="PushContent.Data"/> 即可。
/// </para>
/// <para>
/// ★ <b>只带标题与正文的内容，产出的消息与三参数发法逐字相同</b>（不设 <c>Webpush</c>、不设 <c>Data</c>），
/// 既有调用的线缆形状不因这条路径的存在而变化。
/// </para>
/// <para>
/// 规则是 FCM 的，所以它与 <see cref="FcmTopicName"/> 一样住在这个 FCM 程序集里，而不是父模块的契约层。
/// </para>
/// </remarks>
internal static class FcmPushPayload
{
    /// <summary>链接必须的前缀。</summary>
    private const string HttpsPrefix = "https://";

    /// <summary>
    /// FCM 拒收、或 Android 客户端会从 <c>getData()</c> 里剔掉的整键。
    /// </summary>
    /// <remarks>
    /// FCM v1 文档列的保留词是 <c>from</c> / <c>message_type</c>；<c>collapse_key</c> 是 Android 客户端
    /// 额外剔掉的那一个。比较是 ordinal 的，两边都区分大小写。
    /// </remarks>
    private static readonly string[] ReservedKeys = ["from", "message_type", "collapse_key"];

    /// <summary>
    /// FCM 拒收、或 Android 客户端会剔掉的键前缀。
    /// </summary>
    /// <remarks>
    /// ★ <b>比 FCM 服务端严，是刻意的。</b>FCM 只拒 <c>google.</c> 与 <c>gcm.notification.</c>；
    /// 而 Android 客户端剔掉的是所有 <c>google.</c> 与 <c>gcm.</c> 开头的键。只按服务端的规则放行，
    /// <c>gcm.thread</c> 这样的键会被 FCM 收下、送到设备、然后在 App 读到之前被 SDK 丢掉：
    /// 投递结果是成功的，App 拿不到据以路由的那个值，而两边都没有任何日志。
    /// </remarks>
    private static readonly string[] ReservedKeyPrefixes = ["google.", "gcm."];

    /// <summary>
    /// 判断内容能不能交给 FCM；不能则给出一条<b>带上被拒值</b>的失败原因。
    /// </summary>
    /// <param name="content">要发的内容。</param>
    /// <param name="failureReason">不合法时的失败原因，合法时为 <see langword="null"/>。</param>
    /// <returns>合法返回 <see langword="true"/>。</returns>
    internal static bool TryValidate(PushContent content, out string? failureReason)
    {
        Check.NotNull(content);

        if (!string.IsNullOrWhiteSpace(content.Link) && !IsAbsoluteHttpsUrl(content.Link))
        {
            // 引号是为了让首尾空白、不可见字符这类「肉眼看不出哪里错了」的值在日志里显形。
            failureReason = $"Push link '{content.Link}' is not an absolute HTTPS URL. "
                + "FCM opens the link when a web push notification is clicked and requires an https:// address.";
            return false;
        }

        foreach (var (key, value) in content.Data ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                failureReason = "Push data keys must not be blank.";
                return false;
            }

            if (IsReservedKey(key))
            {
                failureReason = $"Push data key '{key}' is reserved. "
                    + "FCM and its Android client reserve \"from\", \"message_type\", \"collapse_key\" "
                    + "and every key starting with \"google.\" or \"gcm.\".";
                return false;
            }

            // 可空性注解挡不住运行期的 null（未开可空分析的调用方、反序列化出来的字典）。
            if (value is null)
            {
                failureReason = $"Push data value for key '{key}' is null. FCM data values must be strings.";
                return false;
            }
        }

        failureReason = null;
        return true;
    }

    /// <summary>发给单个设备的 FCM 消息。</summary>
    internal static FcmMessage ForDevice(string deviceToken, PushContent content)
    {
        Check.NotNull(content);

        return new FcmMessage
        {
            // FirebaseAdmin 3.6.0 起把 Message.Token 标记为过时并指向 Fid，但两者
            // 不是改名：Token 序列化成 JSON 的 "token"（FCM 注册令牌），Fid 序列化成
            // "fid"（Firebase 安装 ID），是两个不同的标识符和两个不同的线缆字段。
            // SendToAsync 的契约是 deviceToken，消费方传进来的就是客户端 SDK 拿到的
            // 注册令牌，直接改成 Fid 会让所有消费方的推送静默不再送达。改用 FID 需要
            // 消费方改变它们采集与存储的是什么标识符，那是一次契约变更而不是一次版本升级，
            // 因此这里就地抑制过时警告。
#pragma warning disable CS0618 // Type or member is obsolete
            Token = deviceToken,
#pragma warning restore CS0618
            Notification = NotificationOf(content),
            Data = DataOf(content),
            Webpush = WebpushOf(content),
        };
    }

    /// <summary>广播给一个主题的 FCM 消息。</summary>
    internal static FcmMessage ForTopic(string topic, PushContent content)
    {
        Check.NotNull(content);

        return new FcmMessage
        {
            // Topic 与 Token 是 Message 上互斥的寻址字段（FCM 只接受其中一个）。
            // 这里不设 Token —— 主题投递的全部意义就是后端手上一个设备标识符都没有。
            // /topics/ 前缀由 SDK 自己剥掉（Message.FormattedTopic），这里原样传。
            Topic = topic,
            Notification = NotificationOf(content),
            Data = DataOf(content),
            Webpush = WebpushOf(content),
        };
    }

    /// <remarks>
    /// 与 <c>FirebaseAdmin</c> 的 <c>WebpushFcmOptions.CopyAndValidate</c> 对齐：它要求
    /// <c>Uri.IsWellFormedUriString(link, UriKind.Absolute)</c> 且以小写的 <c>https</c> 打头，不满足就抛一句
    /// 不带值的 <c>ArgumentException</c>。这里只比它多要求 <c>://</c>，于是凡是这里放行的值，SDK 那一关也一定过。
    /// </remarks>
    private static bool IsAbsoluteHttpsUrl(string link)
        => link.StartsWith(HttpsPrefix, StringComparison.Ordinal) && Uri.IsWellFormedUriString(link, UriKind.Absolute);

    private static bool IsReservedKey(string key)
        => ReservedKeys.Contains(key, StringComparer.Ordinal)
           || ReservedKeyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal));

    private static FcmNotification NotificationOf(PushContent content)
        => new() { Title = content.Title, Body = content.Body };

    /// <summary>空字典与没有数据同义：不发一个空的 <c>data</c> 对象。</summary>
    private static IReadOnlyDictionary<string, string>? DataOf(PushContent content)
        => content.Data is { Count: > 0 } data ? data : null;

    private static WebpushConfig? WebpushOf(PushContent content)
        => string.IsNullOrWhiteSpace(content.Link)
            ? null
            : new WebpushConfig { FcmOptions = new WebpushFcmOptions { Link = content.Link } };
}
