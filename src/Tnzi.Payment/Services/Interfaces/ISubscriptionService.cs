namespace Tnzi.Payment.Services;

/// <summary>
/// 订阅服务接口
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// 创建订阅
    /// </summary>
    Task<Result<SubscriptionDto>> CreateSubscriptionAsync(CreateSubscriptionDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消订阅
    /// </summary>
    Task<Result> CancelSubscriptionAsync(Guid subscriptionId, CancelSubscriptionDto request, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复订阅
    /// </summary>
    Task<Result> ResumeSubscriptionAsync(Guid subscriptionId, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 变更订阅计划
    /// </summary>
    Task<Result<SubscriptionDto>> ChangePlanAsync(Guid subscriptionId, ChangeSubscriptionDto request, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新支付方式
    /// </summary>
    Task<Result> UpdatePaymentMethodAsync(Guid subscriptionId, string paymentMethodId, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 启用/禁用自动续费
    /// </summary>
    Task<Result> UpdateAutoRenewAsync(Guid subscriptionId, bool autoRenew, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

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
    Task<Result<List<SubscriptionPlanDto>>> GetSubscriptionPlansAsync(bool activeOnly = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建订阅计划
    /// </summary>
    Task<Result<SubscriptionPlanDto>> CreatePlanAsync(SubscriptionPlanDto planDto, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新订阅计划
    /// </summary>
    Task<Result> UpdatePlanAsync(Guid planId, SubscriptionPlanDto planDto, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除订阅计划
    /// </summary>
    Task<Result> DeletePlanAsync(Guid planId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 续费过期订阅
    /// </summary>
    Task<Result<int>> RenewExpiredSubscriptionsAsync(CancellationToken cancellationToken = default);

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
