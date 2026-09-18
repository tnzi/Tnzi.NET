namespace Tnzi.Notification.Services;

/// <summary>
/// 后台队列的一个工作项：要做的事 + 它属于哪个租户。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>租户为什么长在工作项上。</b>队列（<see cref="ChannelQueueService"/>）是 Singleton，
/// 执行时从根容器开一个<b>全新的</b>作用域 —— 里面没有 HttpContext、没有当前用户、也就没有租户。
/// 而 <c>Message</c> 是多租户实体，全局过滤器是严格等值 <c>TenantId == 当前租户</c>，
/// 没有 host 旁路：多租户开启时，租户 A 排进队列的消息在那个作用域里根本查不到，
/// <c>SendAsync</c> 回 404，而这个结果此前被整个丢掉。接口答复「已排队」，行停在 <c>Pending</c>，
/// 一封信都没发出去，日志干净。
/// </para>
/// <para>
/// ★ <b>一处收口。</b>排队发送、定时发送、延迟重试三条入队路径此前各自写一个只捕获 messageId 的闭包；
/// 把「带哪个租户」交给调用方逐处记得，等于把同一件事抄三遍。现在租户在<b>构造</b>时捕获、
/// 在 <see cref="RunAsync"/> 里恢复，队列实现（内置的或消费方自己的）只需调 <see cref="RunAsync"/>；
/// 委托本身不公开，绕不过去。同框架 <c>LocalEventBus</c> 对后台处理器、
/// <c>TenantAwareBackgroundJob</c> 对 Hangfire 任务的做法。
/// </para>
/// </remarks>
public sealed class NotificationWorkItem
{
    private readonly Func<IServiceProvider, CancellationToken, Task> _execute;

    /// <summary>
    /// 初始化一个工作项。
    /// </summary>
    /// <param name="tenantId">
    /// 执行时要切进去的租户；<see langword="null"/> = 没有租户（单租户部署，或多租户下的 host 级消息）。
    /// 入队方应传<b>当时</b>的租户（<c>ICurrentTenant.Id ?? ICurrentUser.TenantId</c>，
    /// 与实体落库时写进 <c>TenantId</c> 的是同一个表达式）。
    /// </param>
    /// <param name="execute">在一个已切好租户的作用域里要做的事。</param>
    public NotificationWorkItem(Guid? tenantId, Func<IServiceProvider, CancellationToken, Task> execute)
    {
        TenantId = tenantId;
        _execute = Check.NotNull(execute);
    }

    /// <summary>执行时要切进去的租户。</summary>
    public Guid? TenantId { get; }

    /// <summary>
    /// 在 <paramref name="scopedServices"/> 里切进 <see cref="TenantId"/> 后执行。
    /// </summary>
    /// <param name="scopedServices">
    /// 队列为这次执行开的作用域。<c>ICurrentTenant</c> 是按作用域注册的，切换只对这个作用域里的
    /// DbContext / 仓储生效 —— 这正是要的效果。容器里没有它（消费方没装多租户基础设施）时照常执行。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RunAsync(IServiceProvider scopedServices, CancellationToken cancellationToken)
    {
        Check.NotNull(scopedServices);

        // 没有租户就不切：与 LocalEventBus / TenantAwareBackgroundJob 同一口径。
        // 这个作用域本来就没有 HttpContext，ICurrentTenant.Id 在这里恒为 null，切成 null 与不切等价。
        var tenant = TenantId.HasValue ? scopedServices.GetService<ICurrentTenant>() : null;
        using (tenant?.Change(TenantId))
        {
            await _execute(scopedServices, cancellationToken);
        }
    }

    /// <summary>
    /// 「把这条消息发出去」—— 三条入队路径共用的那一个工作项。
    /// </summary>
    /// <remarks>
    /// ★ <b>失败的结果要留下日志。</b>此前闭包返回 <c>Task&lt;Result&gt;</c>，隐式转成 <c>Task</c>
    /// 后 <c>Result</c> 被整个丢掉 —— 不抛、不记，这正是租户丢失那条缺陷零症状的原因。
    /// 只记 warning 不抛：抛出去会被队列的 catch 记成「工作项执行出错」，读日志的人会以为是崩了，
    /// 而它是一个说得清原因的业务拒绝（404 / 已取消 / 还没到点）。
    /// </remarks>
    /// <param name="messageId">要发送的消息。</param>
    /// <param name="tenantId">消息所属的租户，见构造函数。</param>
    public static NotificationWorkItem SendMessage(Guid messageId, Guid? tenantId)
        => new(tenantId, async (sp, ct) =>
        {
            var result = await sp.GetRequiredService<INotificationService>().SendAsync(messageId, ct);
            if (!result.Succeeded)
            {
                sp.GetRequiredService<ILogger<NotificationWorkItem>>().LogWarning(
                    "Queued send of notification {MessageId} did not complete: {Message}",
                    messageId, result.Message);
            }
        });
}
