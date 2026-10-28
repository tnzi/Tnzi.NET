namespace Tnzi.Payment.Extensions;

/// <summary>
/// 服务层的账户门：把「经办人」翻译成「他代表的账户」，并按读 / 写判权限。
/// </summary>
public static class BillingAccountResolverExtensions
{
    /// <summary>
    /// <paramref name="actingUserId"/> 为 <see langword="null"/>（管理端 / 系统调用）时不限定账户，返回成功且 <c>Data</c> 为 <see langword="null"/>；
    /// 否则返回他代表的账户，没有对应权限时答 403 <c>BILLING_ACCOUNT_ACCESS_DENIED</c>。
    /// </summary>
    public static async Task<Result<BillingAccountRef?>> ResolveScopeAsync(
        this IBillingAccountResolver resolver, Guid? actingUserId, bool manage, CancellationToken cancellationToken = default)
    {
        Check.NotNull(resolver);

        if (!actingUserId.HasValue)
            return Result.Success<BillingAccountRef?>(null);

        var access = await resolver.ResolveAsync(actingUserId.Value, cancellationToken);
        if (access == null || !(manage ? access.CanManage : access.CanView))
            return Result.Failure<BillingAccountRef?>(ErrorCodes.BillingAccountAccessDenied, 403);

        return Result.Success<BillingAccountRef?>(access.Account);
    }
}
