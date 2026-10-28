namespace Tnzi.Payment.Services;

/// <summary>
/// 内置计费账户解析器：按 <c>Payment:BillingAccount:Scope</c> 划分账户。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>User</b>（默认）：账户就是本人，查看与管理恒为 true：与引入计费账户之前逐字相同。</item>
/// <item><b>Installation</b>：整个安装一个账户（<c>InstallationAccountId</c>），持有 <c>payment.account.manage</c> 的人管理，
///   持有 <c>payment.account.view</c> 或 manage 的人查看。</item>
/// <item><b>Tenant</b>：当前租户一个账户；不在任何租户里的调用者（宿主用户）不代表任何账户。</item>
/// </list>
/// ★ 非 User 范围下没有权限检查器（未加载 Authorization 模块）时，谁都不能查看也不能管理，并记一条 Warning：
/// 那种部署没有任何东西决定「谁是管理者」，放行等于让每个登录用户都能替整个账户付款、退订、删卡。
/// </remarks>
public class DefaultBillingAccountResolver : IBillingAccountResolver
{
    private readonly IOptionsMonitor<BillingAccountOptions> _options;
    private readonly ILogger<DefaultBillingAccountResolver> _logger;
    private readonly IPermissionChecker? _permissionChecker;
    private readonly ICurrentTenant? _currentTenant;

    public DefaultBillingAccountResolver(
        IOptionsMonitor<BillingAccountOptions> options,
        ILogger<DefaultBillingAccountResolver> logger,
        IPermissionChecker? permissionChecker = null,
        ICurrentTenant? currentTenant = null)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _permissionChecker = permissionChecker;
        _currentTenant = currentTenant;
    }

    public async Task<BillingAccountAccess?> ResolveAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;

        BillingAccountRef account;
        switch (options.Scope)
        {
            case BillingAccountScope.User:
                return new BillingAccountAccess(BillingAccountRef.ForUser(userId), CanView: true, CanManage: true);

            case BillingAccountScope.Installation:
                account = new BillingAccountRef(BillingAccountTypes.Installation, options.InstallationAccountId.Trim());
                break;

            case BillingAccountScope.Tenant:
                if (_currentTenant?.Id is not { } tenantId)
                    return null;
                account = new BillingAccountRef(BillingAccountTypes.Tenant, tenantId.ToString());
                break;

            default:
                return null;
        }

        if (_permissionChecker == null)
        {
            _logger.LogWarning(
                "Payment:BillingAccount:Scope is {Scope} but no permission checker is registered, so nobody can view or manage billing account {Account}. Load the Authorization module.",
                options.Scope, account);
            return new BillingAccountAccess(account, CanView: false, CanManage: false);
        }

        var canManage = await _permissionChecker.IsGrantedAsync(userId, PaymentPermissions.AccountManage);
        var canView = canManage || await _permissionChecker.IsGrantedAsync(userId, PaymentPermissions.AccountView);

        return new BillingAccountAccess(account, canView, canManage);
    }
}
