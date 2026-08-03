namespace Tnzi.Payment.Services;

/// <summary>
/// 已保存支付方式（绑卡）服务。
/// </summary>
/// <remarks>
/// 这是 off-session 自动扣款的前置条件：没有绑定过支付方式，
/// 后台续费/试用转正/升级补差都拿不到可用凭据，只能降级 PastDue。
/// <para>
/// 典型前端流程：<br/>
/// 1. <see cref="CreateSetupSessionAsync"/> 取 ClientSecret；<br/>
/// 2. 前端用渠道 SDK 完成支付方式收集（含 3DS 验证）；<br/>
/// 3. <see cref="BindAsync"/> 用渠道返回的 token 登记，落库并同步到订阅快照。
/// </para>
/// </remarks>
public interface IPaymentMethodService
{
    /// <summary>
    /// 创建绑卡会话
    /// </summary>
    Task<Result<SetupSessionDto>> CreateSetupSessionAsync(Guid userId, CreateSetupSessionDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 登记（绑定）支付方式：向渠道校验后落库为用户可复用的支付方式
    /// </summary>
    Task<Result<StoredPaymentMethodDto>> BindAsync(Guid userId, BindPaymentMethodDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户已保存的支付方式列表
    /// </summary>
    Task<Result<List<StoredPaymentMethodDto>>> GetUserMethodsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 设为默认支付方式
    /// </summary>
    Task<Result> SetDefaultAsync(Guid userId, Guid paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除（解绑）支付方式
    /// </summary>
    Task<Result> RemoveAsync(Guid userId, Guid paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户在指定渠道下的默认支付方式（内部调用：订阅创建/变更时取默认卡）
    /// </summary>
    Task<StoredPaymentMethod?> FindDefaultAsync(Guid userId, string channelCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按ID获取用户的支付方式（内部调用）
    /// </summary>
    Task<StoredPaymentMethod?> FindByIdAsync(Guid userId, Guid paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 登记支付方式并返回实体（内部调用：订阅创建时一步完成绑卡）
    /// </summary>
    Task<Result<StoredPaymentMethod>> BindEntityAsync(Guid userId, string channelCode, string paymentMethodToken, string? customerName, string? customerEmail, bool setAsDefault, CancellationToken cancellationToken = default);

    /// <summary>
    /// 标记支付方式最近一次成功使用（内部调用：扣款成功后回写）
    /// </summary>
    Task MarkUsedAsync(Guid paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 渠道侧告知某个已保存支付方式已被移除时，本地跟着失效（内部调用：webhook 回调触发）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="RemoveAsync"/> 的差别：这里**不再回调渠道 detach**——渠道自己就是事件来源，
    /// 再打过去只会 404。找不到对应记录时视为成功（可能是别的系统绑的，或本地早已清理），
    /// 已失效的记录也直接返回成功，让重投的同一事件是幂等的。
    /// </remarks>
    Task<Result> DeactivateByTokenAsync(string channelCode, string token, CancellationToken cancellationToken = default);
}
