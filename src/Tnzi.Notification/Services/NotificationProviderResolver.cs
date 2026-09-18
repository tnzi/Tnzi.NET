namespace Tnzi.Notification.Services;

/// <summary>
/// <see cref="INotificationProviderResolver"/> 的默认实现：默认发送器走普通解析，具名的走 keyed service。
/// </summary>
/// <remarks>
/// <para>
/// 本类是 scoped 的，拿到的 <see cref="IServiceProvider"/> 是当前作用域的，所以解析出来的 scoped 发送器
/// 与同一请求里别处注入的是同一个实例 —— 传真发送器包着的那个邮件发送器也是经这里取的。
/// </para>
/// <para>
/// ★ 容器不支持 keyed service（测试里的 <c>Mock&lt;IServiceProvider&gt;</c> 就不支持）时，具名键一律取不到：
/// 那是「未注册」而不是异常，行为与键写错完全一致。
/// </para>
/// </remarks>
public class NotificationProviderResolver : INotificationProviderResolver
{
    private readonly IServiceProvider _serviceProvider;

    public NotificationProviderResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
    }

    /// <inheritdoc />
    public TSender? Resolve<TSender>(string? providerKey) where TSender : class
    {
        var key = NotificationProviderKeys.Normalize(providerKey);
        if (key == null)
            return _serviceProvider.GetService<TSender>();

        return _serviceProvider is IKeyedServiceProvider keyed
            ? keyed.GetKeyedService<TSender>(key)
            : null;
    }

    /// <inheritdoc />
    public bool IsRegistered(NotificationType type, string? providerKey)
    {
        // 渠道 → 发送器接口的对照表只有这一份；RecipientChannelDispatcher 按 Message.Type 分派时用的是同一组类型。
        return type switch
        {
            NotificationType.Email => Resolve<IEmailSender>(providerKey) != null,
            NotificationType.Sms => Resolve<ISmsSender>(providerKey) != null,
            NotificationType.Push => Resolve<IPushSender>(providerKey) != null,
            NotificationType.Fax => Resolve<IFaxSender>(providerKey) != null,
            _ => false
        };
    }
}
