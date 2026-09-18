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
/// <b>自带一张表</b> <c>Notification_PushDevice</c>（设备令牌注册表），故用
/// <see cref="TnziApplicationModule"/> 并<b>共享父模块的 <c>Notification_</c> 前缀</b>
/// —— 与 <c>IdentityPresenceModule</c> 用 <c>Identity_</c> 同一先例。
/// </para>
/// <para>
/// ★ <b>为什么注册表在这里而不在父模块。</b>不是每个应用都发推送。把这张表或它的契约
/// 放进 <c>Tnzi.Notification</c>，只发邮件的应用会凭空多出一张永远为空的表和一个死端点
/// —— 而那正是当初把推送拆出来的理由。父模块对设备注册<b>一无所知</b>：
/// <c>IPushDeviceService</c> 的契约与实现都在本包，没加载本包时消费方的调用点
/// 直接编译不过，不存在「安静地少发了一批人」的窗口。
/// </para>
/// <para>
/// ★ <b>这张表可以一直是空的，那不是接线断了</b>：只用主题广播
/// （<c>IPushSender.SendToTopicAsync</c>）的应用一个设备标识符都不存。
/// </para>
/// <para>
/// <b>缺席时的行为</b>由父模块决定，见 <c>NotificationModule.PostConfigureServicesAsync</c>：
/// 什么都没配时仍是既有的 <c>NullPushSender</c>（开发期不真发）；
/// <b>配了却没加载本模块</b>时是 <c>UnconfiguredPushSender</c>，每次调用失败并指名要加载哪个包；
/// <b>只配了具名节没配默认节</b>时默认那一格同样是 <c>UnconfiguredPushSender</c>（提示指向配置），
/// 加没加载本模块都一样。后两者是刻意的：一个配置好推送却静默报「已发送」的部署，症状要几周后才出现。
/// </para>
/// </remarks>
[DependsOn(typeof(NotificationModule))]
public class NotificationPushModule : TnziApplicationModule
{
    /// <summary>共享父模块的表前缀：<c>Notification_PushDevice</c>。拆的是程序集不是 schema。</summary>
    public override string? TableNamePrefix => "Notification";

    /// <summary>Notification(40) 之后；实际次序由 <c>[DependsOn]</c> 拓扑排序保证，此值仅为同级 tiebreak。</summary>
    public override int LoadOrder => 41;

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 普通 Configure 阶段注册即可胜出：父模块的回退在 PostConfigure 阶段用 TryAdd 补位，
        // 那一步跑在所有模块的 Configure 之后，看到已有注册就不再插手。
        // ★ 分档按父模块定的规则走，加载了本包并不改变它：
        //   什么都没配 = 这个部署不发推送 → NullPushSender（只用主题广播的部署也会加载本包，不能因此失败）；
        //   只配了具名节没配默认节 = 显然要推送，没带键的消息不能落进报成功的空实现 → 失败并指向配置
        //   （与邮件 / 短信同一分档；这里包已加载，提示说的是配置不是包）。
        context.Services.AddScoped<IPushSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
            if (options.PushSender != null)
                return NewPushSender(sp, options.PushSender, providerKey: null);

            if (options.PushSenders.Count > 0)
                return UnconfiguredPushSender.ForMissingDefault(sp.GetRequiredService<ILogger<UnconfiguredPushSender>>());

            return new NullPushSender(sp.GetRequiredService<ILogger<NullPushSender>>());
        });

        // 具名推送节：每个键一个 PushSender，各自引导一个以键命名的 FirebaseApp。
        foreach (var key in NotificationProviderProfiles.NamedKeys(context.Configuration, "PushSenders"))
        {
            context.Services.AddKeyedScoped<IPushSender>(key, (sp, _) =>
            {
                var options = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
                return NewPushSender(sp, NotificationProviderProfiles.Get(options.PushSenders, key, "PushSenders"), key);
            });
        }

        context.Services.AddScoped<IPushDeviceService, PushDeviceService>();

        // 权限码随模块走：不加载本模块的宿主永远不会 seed 这两个码。
        context.Services.AddTransient<IPermissionDefinitionProvider, NotificationPushPermissions>();

        return Task.CompletedTask;
    }

    private static IPushSender NewPushSender(IServiceProvider sp, PushSenderOptions profile, string? providerKey)
    {
        // IPushDeviceService 用 GetService 而不是构造参数注入：PushSender 的构造签名
        // 是 (options, logger) 的公开形状，消费方可能自己 new 它（测试里就有）。
        // 拿不到时投递照常，只是死令牌不会被退役 —— 那是可降级的，投递本身不受影响。
        return new PushSender(profile, sp.GetRequiredService<ILogger<PushSender>>(), sp.GetService<IPushDeviceService>(), providerKey);
    }
}
