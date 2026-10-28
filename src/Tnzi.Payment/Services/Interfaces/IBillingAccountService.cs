namespace Tnzi.Payment.Services;

/// <summary>
/// 计费账户的账单资料（发票抬头与账单联系人）。
/// </summary>
public interface IBillingAccountService
{
    /// <summary>
    /// 用户 <paramref name="userId"/> 所代表的账户与它的资料（需要查看权限）。
    /// </summary>
    Task<Result<BillingAccountDto>> GetCurrentAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新用户 <paramref name="userId"/> 所代表的账户的资料（需要管理权限）。
    /// </summary>
    Task<Result<BillingAccountDto>> UpdateCurrentAsync(Guid userId, UpdateBillingAccountDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 账单联系人：账户填了资料就用资料（名称 + 全部联系邮箱），没填或缺项时用 <paramref name="fallbackName"/> /
    /// <paramref name="fallbackEmail"/>（通常是经办人或订阅上的快照）补齐。内部调用：开票、催款、渠道侧客户的名字与邮箱。
    /// </summary>
    Task<BillingContact> ResolveContactAsync(BillingAccountRef? account, string? fallbackName, string? fallbackEmail, CancellationToken cancellationToken = default);

    /// <summary>
    /// 同 <see cref="ResolveContactAsync(BillingAccountRef?, string?, string?, CancellationToken)"/>，账户资料与
    /// <paramref name="fallbackEmail"/> 都给不出地址时，再按 <paramref name="fallbackUserId"/> 查这个用户账号上的邮箱。
    /// </summary>
    /// <remarks>
    /// 令牌不一定带邮箱声明，后台任务（续费、催款、自动开票）又没有当前用户，手里只有发起人的 Id。
    /// 没有这一步，一家刚开通、还没填账单联系人的机构收不到任何收据与催款信。
    /// 默认实现不查（与旧重载相同），框架实现经可选的 <c>IUserEmailProvider</c> 查。
    /// <para>
    /// 框架实现逐项取值：账户自己的资料 → 消费应用注册的 <see cref="IBillingAccountProfileProvider"/>（名称、公司、地址、税号、邮箱）
    /// → 经办用户。落到用户身上时名称取这个人的显示名（可选的 <c>IUserDisplayNameProvider</c>），查不到才用
    /// <paramref name="fallbackName"/>：调用方给的后备名通常是登录名，而票据上该印人名。
    /// </para>
    /// </remarks>
    Task<BillingContact> ResolveContactAsync(BillingAccountRef? account, string? fallbackName, string? fallbackEmail, Guid? fallbackUserId, CancellationToken cancellationToken = default)
        => ResolveContactAsync(account, fallbackName, fallbackEmail, cancellationToken);
}
