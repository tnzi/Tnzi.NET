namespace Tnzi.Payment.Services;

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
    /// 获取订阅信息
    /// </summary>
    Task<Result<SubscriptionDto>> GetSubscriptionAsync(Guid id, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户订阅列表
    /// </summary>
    Task<Result<IPagedList<SubscriptionDto>>> GetUserSubscriptionsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取订阅列表
    /// </summary>
    Task<Result<IPagedList<SubscriptionDto>>> GetSubscriptionListAsync(SubscriptionQueryDto query, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取订阅计划列表
    /// </summary>
    Task<Result<List<SubscriptionPlanDto>>> GetSubscriptionPlansAsync(bool activeOnly = true, string? productCode = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建订阅计划
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
}
