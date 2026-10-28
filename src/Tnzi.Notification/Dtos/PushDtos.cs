namespace Tnzi.Notification.Dtos;

/// <summary>
/// 一条推送要说的全部内容：标题、正文，外加可选的点击目标与机读数据。地址（设备令牌或主题）不在这里。
/// </summary>
/// <remarks>
/// <para>
/// <b>点击目标与数据是两件事，分别服务两类客户端。</b><see cref="Link"/> 是 web 推送被点开时要打开的地址
/// （FCM 的 <c>webpush.fcm_options.link</c>），原生客户端没有对应的字段；<see cref="Data"/> 是发给所有平台的
/// 键值对，原生客户端通常按它路由一次点击。要让原生端也打开同一页，把路由信息同时放进 <see cref="Data"/>。
/// </para>
/// <para>
/// 只带标题与正文的内容与 <see cref="IPushSender.SendToAsync(string, string, string, CancellationToken)"/>
/// 那一种发法逐字等价：发送器拿到的消息形状一模一样。
/// </para>
/// </remarks>
/// <param name="Title">通知标题。</param>
/// <param name="Body">通知正文。</param>
public sealed record PushContent(string Title, string Body)
{
    private readonly IReadOnlyDictionary<string, string>? _data;

    /// <summary>
    /// 获取或设置 点击通知时要打开的地址（web 推送）。空白视为没有。
    /// </summary>
    /// <remarks>
    /// 内置的 FCM 实现要求它是一个<b>绝对的 https 地址</b>，不合法时整条推送以一条读得懂的失败结束，
    /// 不会退化成「不带链接照发」：那样用户点开的是首页，而投递结果是成功的。
    /// </remarks>
    public string? Link { get; init; }

    /// <summary>
    /// 获取或设置 随推送下发的机读键值对（所有平台都收得到）。<see langword="null"/> 或空字典视为没有。
    /// </summary>
    /// <remarks>
    /// 赋值时就复制一份只读快照：调用方事后改动自己的字典，不会改动这条已经交出去的内容。
    /// 内置的 FCM 实现拒绝 FCM 与 Android 客户端保留的键，见 <c>Tnzi.Notification.Push</c> 的文档。
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Data
    {
        get => _data;
        init => _data = value == null ? null : new Dictionary<string, string>(value).AsReadOnly();
    }

    /// <summary>
    /// 是否带着标题与正文以外的东西（点击目标或数据）。
    /// </summary>
    /// <remarks>
    /// 为 <see langword="false"/> 时这条内容与三参数的发法等价，只认三参数方法的发送器照样发得出去；
    /// 为 <see langword="true"/> 时转交给三参数方法就会把点击目标与数据安静地丢掉。
    /// </remarks>
    internal bool HasLinkOrData => !string.IsNullOrWhiteSpace(Link) || Data is { Count: > 0 };
}
