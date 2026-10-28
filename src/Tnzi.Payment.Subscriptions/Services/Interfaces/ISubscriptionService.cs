namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// 订阅服务接口
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// 创建订阅。返回订阅本体与首期支付凭据（需要收款时），供前端直接拉起收银台。
    /// </summary>
    Task<Result<SubscriptionCreateResultDto>> CreateSubscriptionAsync(CreateSubscriptionDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消订阅
    /// </summary>
    Task<Result> CancelSubscriptionAsync(Guid subscriptionId, CancelSubscriptionDto request, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 暂停订阅（到期自动恢复或手动恢复）
    /// </summary>
    Task<Result> PauseSubscriptionAsync(Guid subscriptionId, PauseSubscriptionDto request, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复订阅（暂停或已取消的订阅重新激活）
    /// </summary>
    Task<Result> ResumeSubscriptionAsync(Guid subscriptionId, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 绑定/更新订阅使用的支付方式
    /// </summary>
    Task<Result<SubscriptionDto>> UpdatePaymentMethodAsync(Guid subscriptionId, AttachPaymentMethodDto request, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 启用/禁用自动续费
    /// </summary>
    Task<Result> UpdateAutoRenewAsync(Guid subscriptionId, bool autoRenew, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 立即重试扣款（逾期欠费的订阅在用户换卡后主动挽回）
    /// </summary>
    Task<Result> RetryBillingAsync(Guid subscriptionId, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 当场补缴一条逾期欠费（PastDue）或已暂停服务（Suspended）的订阅：返回一张有人在场的收银台单（可做 3-D Secure），
    /// 付款时同时留存这张卡。已有一张补缴单还能付时返回同一张单的凭据。需要能管理订阅所属的计费账户。
    /// 对还没付首期的订阅（Pending）返回那张首期单的凭据，让放弃过一次收银台的管理者接着付完，而不是再开一条订阅；
    /// 首期单已经不能付了（失败、过期、关闭）时答 400 <c>SUBSCRIPTION_CANNOT_PAY_NOW</c>，重新开通会先收口那条 Pending。
    /// </summary>
    Task<Result<SubscriptionPayNowResultDto>> PayNowAsync(Guid subscriptionId, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 计费账户（在某个产品下）此刻的境况：有一条订阅正常即 Good；否则暂停服务 &gt; 欠费 &gt; 无。内部调用（门禁、提示）。
    /// </summary>
    Task<BillingStanding> GetStandingAsync(BillingAccountRef account, string? productCode = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 境况连同需要处理的那条订阅（逾期或已暂停：计划、欠款、截止日）。不判权限、不填 <c>CanView</c> / <c>CanManage</c>：
    /// 那是 <see cref="ISubscriptionAccessGate.GetStandingAsync"/> 按调用者补上的。内部调用。
    /// </summary>
    Task<BillingStandingDto> GetStandingDetailAsync(BillingAccountRef account, string? productCode = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取订阅信息
    /// </summary>
    Task<Result<SubscriptionDto>> GetSubscriptionAsync(Guid id, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户订阅列表（按创建时间倒序分页；<paramref name="query"/> 为 null 时取最新 100 条）
    /// </summary>
    Task<Result<IPagedList<SubscriptionDto>>> GetUserSubscriptionsAsync(Guid userId, PagedQueryDto? query = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取订阅列表
    /// </summary>
    Task<Result<IPagedList<SubscriptionDto>>> GetSubscriptionListAsync(SubscriptionQueryDto query, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取订阅计划列表。<paramref name="includeTax"/> 为 true 时每个计划带上一期标价的计税结果（<see cref="SubscriptionPlanDto.Tax"/>）。
    /// </summary>
    Task<Result<List<SubscriptionPlanDto>>> GetSubscriptionPlansAsync(bool activeOnly = true, string? productCode = null, bool includeTax = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// 新建计划时表单的预填值（默认币种）
    /// </summary>
    Result<SubscriptionPlanDefaultsDto> GetPlanDefaults();

    /// <summary>
    /// 创建订阅计划。币种留空时取 <c>Payment:DefaultCurrency</c>。
    /// </summary>
    Task<Result<SubscriptionPlanDto>> CreatePlanAsync(SubscriptionPlanDto planDto, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新订阅计划
    /// </summary>
    Task<Result> UpdatePlanAsync(Guid planId, SubscriptionPlanDto planDto, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停用订阅计划
    /// </summary>
    Task<Result> DeletePlanAsync(Guid planId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 续费过期订阅（后台任务调用：对到期且自动续费的订阅发起 off-session 扣款，成功才推进周期，失败降级 PastDue）
    /// </summary>
    Task<Result<int>> RenewExpiredSubscriptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 处理试用到期（后台任务调用：试用期满的订阅转正扣款或过期）
    /// </summary>
    Task<Result<int>> ConvertDueTrialsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 过期到期订阅（后台任务调用：到期未续费/逾期超宽限期的订阅置为 Expired）
    /// </summary>
    Task<Result<int>> ExpireOverdueSubscriptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复暂停到期的订阅（后台任务调用）
    /// </summary>
    Task<Result<int>> ResumeDuePausedSubscriptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送续费提醒（后台任务调用：在下次扣款前 N 天提醒用户）
    /// </summary>
    Task<Result<int>> SendRenewalRemindersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 结算已到生效日期的待生效计划变更（后台任务调用）。
    /// </summary>
    /// <remarks>
    /// 覆盖的是**不走续费路径**的订阅（关掉自动续费、暂停中、逾期欠费）——
    /// 走续费路径的那些由 <see cref="RenewExpiredSubscriptionsAsync"/> 在扣款**之前**逐条结算，
    /// 因为降级必须先于按新价扣款生效，而扫描之间的先后不由本接口保证。
    /// </remarks>
    Task<Result<int>> ApplyDuePlanChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 支付完成回流：根据计费用途推进订阅状态机（激活/续费/试用转正/升级补差生效）。由支付完成事件处理器调用。
    /// </summary>
    Task<Result> ApplyPaymentCompletedAsync(SubscriptionPaymentContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 支付失败/过期回流：续费/转正失败降级 PastDue 并累计重试；升级补差失败取消变更。由支付失败事件处理器调用。
    /// </summary>
    Task<Result> ApplyPaymentFailedAsync(SubscriptionPaymentContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 变更订阅计划（含按比例计费）
    /// </summary>
    Task<Result<SubscriptionChangeDto>> ChangeSubscriptionPlanAsync(Guid subscriptionId, ChangeSubscriptionPlanDto input, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 预览计划变更（按比例计费预览，不持久化）
    /// </summary>
    Task<Result<SubscriptionChangeDto>> GetPlanChangePreviewAsync(Guid subscriptionId, Guid newPlanId, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消待生效的计划变更
    /// </summary>
    Task<Result> CancelPendingChangeAsync(Guid changeId, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 重排订阅计划的展示顺序（拖拽排序）
    /// </summary>
    /// <param name="planIds">按新顺序排列的计划 Id，可以只是当前可见的一段</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<Result> ReorderPlansAsync(IReadOnlyList<Guid> planIds, CancellationToken cancellationToken = default);
}
