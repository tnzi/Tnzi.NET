namespace Tnzi.Notification.Push;

/// <summary>
/// 推送渠道子模块：把 Firebase Cloud Messaging 的实现从 <c>Tnzi.Notification</c> 里分出来。
/// </summary>
/// <remarks>
/// <para>
/// <b>业务范围</b>：向移动设备投递推送。只发邮件或短信的应用不加载它，
/// 于是 <c>FirebaseAdmin</c> 及其 5 个 <c>Google.*</c> 传递包不进入依赖闭包。
/// </para>
/// <para>
/// <b>无实体无表</b>，故用 <see cref="TnziCustomModule"/> 且不声明表前缀；
/// 无控制器、无权限码，加载与否不改变任何 HTTP 面。
/// </para>
/// <para>
/// <b>缺席时的行为</b>由父模块决定，见 <c>NotificationModule.PostConfigureServicesAsync</c>：
/// 没配 <c>Notification:PushSender</c> 时仍是既有的 <c>NullPushSender</c>（开发期不真发）；
/// <b>配了却没加载本模块</b>时是 <c>UnconfiguredPushSender</c>，每次调用失败并指名要加载哪个包。
/// 后者是刻意的：一个配置好推送却静默报「已发送」的部署，症状要几周后才出现。
/// </para>
/// </remarks>
[DependsOn(typeof(NotificationModule))]
public class NotificationPushModule : TnziCustomModule
{
    /// <summary>Notification(40) 之后；实际次序由 <c>[DependsOn]</c> 拓扑排序保证，此值仅为同级 tiebreak。</summary>
    public override int LoadOrder => 41;

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 普通 Configure 阶段注册即可胜出：父模块的回退在 PostConfigure 阶段用 TryAdd 补位，
        // 那一步跑在所有模块的 Configure 之后，看到已有注册就不再插手。
        context.Services.AddScoped<IPushSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<PushSender>>();
            return new PushSender(options, logger);
        });

        return Task.CompletedTask;
    }
}
