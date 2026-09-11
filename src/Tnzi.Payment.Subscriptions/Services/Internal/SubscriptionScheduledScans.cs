namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// 订阅域贡献给支付后台循环的五条扫描。
/// </summary>
/// <remarks>
/// <para>
/// 拆分前这五条是 <c>PaymentBackgroundService</c> 里写死的五行 <c>subscriptionService.XxxAsync()</c>，
/// 那意味着后台服务必须认识 <c>ISubscriptionService</c>。改成经
/// <see cref="IPaymentScheduledScan"/> 贡献之后，后台循环只知道「有一批扫描要跑」，
/// 不加载本包就是少五条，父模块自己那两条照跑。
/// </para>
/// <para>
/// 名字（<see cref="IPaymentScheduledScan.Name"/>）与拆分前的日志文案<b>逐字相同</b>，
/// 运维手上按日志串搜的脚本不用改。
/// </para>
/// <para>
/// 五个类嵌在一个外层类里，纯粹是为了让「它们是同一批东西」在文件与注册处都看得出来；
/// 每个都是独立注册的 scoped 服务，各自有自己的 <c>try</c> 隔离（见调用方）。
/// </para>
/// </remarks>
public static class SubscriptionScheduledScans
{
    /// <summary>五条扫描共用的基类：拿到订阅服务，剩下的交给子类点名调哪个方法。</summary>
    public abstract class SubscriptionScanBase : IPaymentScheduledScan
    {
        private readonly ISubscriptionService _subscriptionService;

        protected SubscriptionScanBase(ISubscriptionService subscriptionService)
        {
            _subscriptionService = Check.NotNull(subscriptionService);
        }

        /// <summary>订阅服务，供子类调用。</summary>
        protected ISubscriptionService SubscriptionService => _subscriptionService;

        /// <inheritdoc />
        public abstract string Name { get; }

        /// <inheritdoc />
        public abstract Task<Result<int>> RunAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>续费到期订阅（off-session 扣款）。</summary>
    public sealed class RenewDueSubscriptions : SubscriptionScanBase
    {
        public RenewDueSubscriptions(ISubscriptionService subscriptionService) : base(subscriptionService) { }

        /// <inheritdoc />
        public override string Name => "renew due subscriptions";

        /// <inheritdoc />
        public override Task<Result<int>> RunAsync(CancellationToken cancellationToken = default)
            => SubscriptionService.RenewExpiredSubscriptionsAsync(cancellationToken);
    }

    /// <summary>试用到期转正 / 过期。</summary>
    public sealed class ConvertDueTrials : SubscriptionScanBase
    {
        public ConvertDueTrials(ISubscriptionService subscriptionService) : base(subscriptionService) { }

        /// <inheritdoc />
        public override string Name => "convert due trials";

        /// <inheritdoc />
        public override Task<Result<int>> RunAsync(CancellationToken cancellationToken = default)
            => SubscriptionService.ConvertDueTrialsAsync(cancellationToken);
    }

    /// <summary>暂停到期自动恢复。</summary>
    public sealed class ResumeDuePausedSubscriptions : SubscriptionScanBase
    {
        public ResumeDuePausedSubscriptions(ISubscriptionService subscriptionService) : base(subscriptionService) { }

        /// <inheritdoc />
        public override string Name => "resume due paused subscriptions";

        /// <inheritdoc />
        public override Task<Result<int>> RunAsync(CancellationToken cancellationToken = default)
            => SubscriptionService.ResumeDuePausedSubscriptionsAsync(cancellationToken);
    }

    /// <summary>到期未续费 / 逾期超宽限期 → 过期。</summary>
    public sealed class ExpireOverdueSubscriptions : SubscriptionScanBase
    {
        public ExpireOverdueSubscriptions(ISubscriptionService subscriptionService) : base(subscriptionService) { }

        /// <inheritdoc />
        public override string Name => "expire overdue subscriptions";

        /// <inheritdoc />
        public override Task<Result<int>> RunAsync(CancellationToken cancellationToken = default)
            => SubscriptionService.ExpireOverdueSubscriptionsAsync(cancellationToken);
    }

    /// <summary>
    /// 结算已到生效日期的待生效计划变更。
    /// </summary>
    /// <remarks>
    /// 覆盖的是**不走续费路径**的订阅（关掉自动续费、暂停中、逾期欠费）。
    /// 走续费路径的那些由 <c>RenewExpiredSubscriptionsAsync</c> 在扣款**之前**逐条结算 ——
    /// 降级必须先于按新价扣款生效，而这里的扫描之间没有任何顺序保证：
    /// 把「先结算再扣款」交给扫描注册顺序，等于把一条资金正确性依赖挂在一行 DI 注册的位置上。
    /// </remarks>
    public sealed class ApplyDuePlanChanges : SubscriptionScanBase
    {
        public ApplyDuePlanChanges(ISubscriptionService subscriptionService) : base(subscriptionService) { }

        /// <inheritdoc />
        public override string Name => "apply due plan changes";

        /// <inheritdoc />
        public override Task<Result<int>> RunAsync(CancellationToken cancellationToken = default)
            => SubscriptionService.ApplyDuePlanChangesAsync(cancellationToken);
    }

    /// <summary>续费提醒：在扣款前 N 天通知用户，尤其是尚未绑卡的。</summary>
    public sealed class SendRenewalReminders : SubscriptionScanBase
    {
        public SendRenewalReminders(ISubscriptionService subscriptionService) : base(subscriptionService) { }

        /// <inheritdoc />
        public override string Name => "send renewal reminders";

        /// <inheritdoc />
        public override Task<Result<int>> RunAsync(CancellationToken cancellationToken = default)
            => SubscriptionService.SendRenewalRemindersAsync(cancellationToken);
    }
}
