namespace Tnzi.Notification.Services.Internal;

/// <summary>
/// 对外输出收件人时，把<b>推送渠道</b>的地址打掩码。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>推送的「地址」是设备令牌，不是邮箱或电话号码。</b>另外三条渠道的地址本来就是
/// 消费方自己录进来的联系方式，管理端看得见它天经地义；推送这一列存的却是 FCM 发给
/// 某个 App 安装的注册令牌 —— 它是那台设备的<b>投递地址凭据</b>：持有它就能改写或
/// 掐掉这台设备的推送归属（见 <c>Tnzi.Notification.Push</c> 的注册表）。
/// 一次投递报告查询把整批令牌原文交给任何持 <c>notification.message.view</c> 的人，
/// 与 Push 子模块自己在设备列表里坚持只给 <c>TokenMask</c> 直接矛盾。
/// </para>
/// <para>
/// ★ <b>掩码规则只有一份</b>（<see cref="PushTokenMask"/>，就住在本模块）。
/// 父模块不能引用子模块，而在两边各写一遍「保留尾部 8 位」正是这个模块的规则漂开过的方式。
/// </para>
/// <para>
/// ★ <b>作用在 DTO 上而不是在每个查询里</b>：<c>NotificationInfo</c> 同时带着
/// <c>Type</c> 与 <c>Recipients</c>，所以无论这份 DTO 是 <c>MapTo</c> 出来的还是
/// <c>ProjectTo</c> 在 SQL 里投影出来的，这一步都成立。
/// </para>
/// </remarks>
internal static class RecipientAddressMask
{
    /// <summary>给一份通知的收件人地址打掩码，返回同一个实例。</summary>
    public static NotificationInfo Apply(NotificationInfo info)
    {
        Mask(info.Type, info.Recipients);
        return info;
    }

    /// <summary>给一批通知打掩码，返回同一个实例。</summary>
    public static TList Apply<TList>(TList infos) where TList : IEnumerable<NotificationInfo>
    {
        foreach (var info in infos)
            Mask(info.Type, info.Recipients);

        return infos;
    }

    /// <summary>给一页通知打掩码，返回同一个实例。</summary>
    public static IPagedList<NotificationInfo> Apply(IPagedList<NotificationInfo> page)
    {
        Apply(page.Items);
        return page;
    }

    /// <summary>给一份投递报告打掩码，返回同一个实例。</summary>
    public static DeliveryReportDto Apply(DeliveryReportDto report)
    {
        Mask(report.Type, report.Recipients);
        return report;
    }

    private static void Mask(NotificationType type, IEnumerable<RecipientOutput>? recipients)
    {
        if (type != NotificationType.Push || recipients == null)
            return;

        foreach (var recipient in recipients)
            recipient.Address = PushTokenMask.Of(recipient.Address);
    }
}
