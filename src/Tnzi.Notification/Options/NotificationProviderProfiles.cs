namespace Tnzi.Notification.Options;

/// <summary>
/// 具名发送器配置节（<c>Notification:MailSenders:{key}</c> 这一族）的两个读法：启动时列键、解析时取节。
/// </summary>
/// <remarks>
/// <para>
/// 启动时必须直接读 <see cref="IConfiguration"/> 而不是 <c>IOptions</c>：keyed service 要在 Configure 阶段
/// 逐键登记，那时容器还没建起来。解析时则回到绑定好的 <see cref="NotificationOptions"/>，
/// 与默认发送器读配置的方式一致，也让校验器（<c>ValidateOnStart</c>）在第一次解析时就把配错的节报出来。
/// </para>
/// <para>
/// 两处的键都经 <see cref="NotificationProviderKeys.Normalize"/> 收口，所以 <c>MailSenders:Marketing</c>
/// 登记出来的 keyed service 键是 <c>marketing</c>，与消息落库的键同一写法。
/// </para>
/// <para>
/// 放在父模块且 internal：<c>Tnzi.Notification.Push</c> 登记具名推送发送器时用同一份，
/// 两个模块各写一遍正是这类「按键对号」的规则漂开的方式。
/// </para>
/// </remarks>
internal static class NotificationProviderProfiles
{
    /// <summary>
    /// 一条渠道的具名节名（<c>MailSenders</c> 这一族），给失败原因与日志指路用 ——
    /// 「服务商未注册」的下一句必须是「去哪里配」，而那个节名按渠道不同。
    /// </summary>
    public static string SectionFor(NotificationType type) => type switch
    {
        NotificationType.Email => "MailSenders",
        NotificationType.Sms => "SmsSenders",
        NotificationType.Push => "PushSenders",
        NotificationType.Fax => "FaxSenders",
        _ => $"{type}Senders"
    };

    /// <summary>「键 X 在渠道 Y 上没注册」的标准说法，创建路径与派发路径共用一份措辞。</summary>
    public static string NotRegisteredMessage(NotificationType type, string? providerKey)
        => $"Notification provider '{providerKey ?? NotificationProviderKeys.Default}' is not registered for {type}. "
           + $"Configure it under Notification:{SectionFor(type)}:{providerKey ?? NotificationProviderKeys.Default} "
           + "or register a keyed sender with that key (AddNotificationSender).";

    /// <summary>
    /// 配置里 <c>Notification:{section}</c> 下声明的具名键（已收口、去重）。
    /// 收口后落到默认键上的（<c>default</c>）不在其列 —— 校验器会在启动时把那一节报成错误。
    /// </summary>
    public static IReadOnlyList<string> NamedKeys(IConfiguration configuration, string section)
    {
        Check.NotNull(configuration);
        Check.NotNullOrWhiteSpace(section);

        return configuration.GetSection($"Notification:{section}").GetChildren()
            .Select(child => NotificationProviderKeys.Normalize(child.Key))
            .Where(key => key != null)
            .Distinct()
            .ToList()!;
    }

    /// <summary>
    /// 从绑定好的字典里按<b>收口后的</b>键取一节。
    /// </summary>
    /// <exception cref="ConfigurationException">
    /// 登记时还在、解析时不见了 —— 配置源在两次读取之间变了。这不是「退回默认发送器」的理由：
    /// 消息指定这一家是有原因的。
    /// </exception>
    public static TProfile Get<TProfile>(IReadOnlyDictionary<string, TProfile> profiles, string key, string section)
        where TProfile : class
    {
        Check.NotNull(profiles);
        Check.NotNullOrWhiteSpace(key);

        foreach (var (candidate, profile) in profiles)
        {
            if (NotificationProviderKeys.Normalize(candidate) == key)
                return profile;
        }

        throw new ConfigurationException(
            $"Notification:{section}:{key}",
            $"Named sender profile '{key}' was registered at startup but is no longer present in Notification:{section}.");
    }
}
