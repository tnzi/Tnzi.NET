namespace Tnzi.Notification.Extensions;

/// <summary>
/// 给一条渠道注册<b>具名</b>发送器（SendGrid / SES / 阿里云短信这类框架没内置的服务商）。
/// </summary>
/// <remarks>
/// <para>
/// 具名发送器就是同一接口的 keyed service，这两个方法只多做一件事：把键经
/// <see cref="NotificationProviderKeys.Normalize"/> 收口成规范形态（小写、非 <c>default</c>）再登记，
/// 好与消息落库的键、配置节登记的键对得上。直接调 <c>AddKeyedScoped</c> 也行，键要自己写成小写。
/// </para>
/// <para>
/// 默认发送器<b>不经这里</b>：它就是普通的 <c>AddScoped&lt;IEmailSender, MySender&gt;()</c>，
/// 在消费方模块的 Configure 阶段注册即可赢过框架的 <c>TryAdd</c>。
/// </para>
/// <para>
/// 与配置节同键时<b>后注册的赢</b>（消费方模块的 LoadOrder 在本模块之后，所以是代码赢过配置）。
/// </para>
/// </remarks>
public static class NotificationSenderServiceCollectionExtensions
{
    /// <summary>
    /// 以 <paramref name="providerKey"/> 为键注册一个 <typeparamref name="TSender"/> 渠道的具名发送器。
    /// </summary>
    /// <typeparam name="TSender"><c>IEmailSender</c> / <c>ISmsSender</c> / <c>IPushSender</c> / <c>IFaxSender</c> 之一。</typeparam>
    /// <typeparam name="TImplementation">实现类型，按 scoped 生命周期由容器构造。</typeparam>
    public static IServiceCollection AddNotificationSender<TSender, TImplementation>(this IServiceCollection services, string providerKey)
        where TSender : class
        where TImplementation : class, TSender
    {
        Check.NotNull(services);
        var key = RequireNamedKey(providerKey);
        services.AddKeyedScoped<TSender, TImplementation>(key);
        return services;
    }

    /// <summary>
    /// 以 <paramref name="providerKey"/> 为键注册一个 <typeparamref name="TSender"/> 渠道的具名发送器，实例由 <paramref name="factory"/> 构造。
    /// </summary>
    public static IServiceCollection AddNotificationSender<TSender>(this IServiceCollection services, string providerKey, Func<IServiceProvider, TSender> factory)
        where TSender : class
    {
        Check.NotNull(services);
        Check.NotNull(factory);
        var key = RequireNamedKey(providerKey);
        services.AddKeyedScoped<TSender>(key, (sp, _) => factory(sp));
        return services;
    }

    /// <summary>
    /// 键必须是一个合法的<b>具名</b>键：默认发送器不走这里，形状不合法的键也不登记 ——
    /// 登记一个消息永远指不到的键，与没登记的症状一模一样。
    /// </summary>
    private static string RequireNamedKey(string providerKey)
    {
        Check.NotNullOrWhiteSpace(providerKey);

        var key = NotificationProviderKeys.Normalize(providerKey)
            ?? throw new ArgumentException(
                $"'{providerKey}' is the default provider; register the default sender with AddScoped<TSender, TImplementation>() instead.",
                nameof(providerKey));

        if (NotificationProviderKeys.Describe(key) is { } error)
            throw new ArgumentException(error, nameof(providerKey));

        return key;
    }
}
