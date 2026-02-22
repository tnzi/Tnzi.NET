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
    Task<Result> CancelSubscriptionAsync(Guid subscriptionId, CancelSubscriptionDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复订阅
    /// </summary>
    Task<Result> ResumeSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 变更订阅计划
    /// </summary>
    Task<Result<SubscriptionDto>> ChangePlanAsync(Guid subscriptionId, ChangeSubscriptionDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新支付方式
    /// </summary>
    Task<Result> UpdatePaymentMethodAsync(Guid subscriptionId, string paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 启用/禁用自动续费
    /// </summary>
    Task<Result> UpdateAutoRenewAsync(Guid subscriptionId, bool autoRenew, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取订阅信息
    /// </summary>
    Task<Result<SubscriptionDto>> GetSubscriptionAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户订阅列表
    /// </summary>
    Task<Result<IPagedList<SubscriptionDto>>> GetUserSubscriptionsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取订阅列表
    /// </summary>
    Task<Result<IPagedList<SubscriptionDto>>> GetSubscriptionListAsync(SubscriptionQueryDto query, CancellationToken cancellationToken = default);

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
}
