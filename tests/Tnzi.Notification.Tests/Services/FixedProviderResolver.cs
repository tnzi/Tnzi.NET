namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// 单元测试用的 <see cref="INotificationProviderResolver"/>：四条渠道各一个固定的默认发送器，
/// 外加按键登记的具名发送器。不经容器，好让构造 <c>NotificationService</c> 的测试继续只用 mock。
/// </summary>
internal sealed class FixedProviderResolver : INotificationProviderResolver
{
    private readonly Dictionary<(Type Sender, string? Key), object> _senders = new();

    public FixedProviderResolver(IEmailSender email, ISmsSender sms, IPushSender push, IFaxSender fax)
    {
        _senders[(typeof(IEmailSender), null)] = email;
        _senders[(typeof(ISmsSender), null)] = sms;
        _senders[(typeof(IPushSender), null)] = push;
        _senders[(typeof(IFaxSender), null)] = fax;
    }

    /// <summary>登记一个具名发送器（键按规范形态收口）。</summary>
    public FixedProviderResolver With<TSender>(string providerKey, TSender sender) where TSender : class
    {
        _senders[(typeof(TSender), NotificationProviderKeys.Normalize(providerKey))] = sender;
        return this;
    }

    public TSender? Resolve<TSender>(string? providerKey) where TSender : class
        => _senders.TryGetValue((typeof(TSender), NotificationProviderKeys.Normalize(providerKey)), out var sender)
            ? (TSender)sender
            : null;

    public bool IsRegistered(NotificationType type, string? providerKey)
        => type switch
        {
            NotificationType.Email => Resolve<IEmailSender>(providerKey) != null,
            NotificationType.Sms => Resolve<ISmsSender>(providerKey) != null,
            NotificationType.Push => Resolve<IPushSender>(providerKey) != null,
            NotificationType.Fax => Resolve<IFaxSender>(providerKey) != null,
            _ => false
        };
}
