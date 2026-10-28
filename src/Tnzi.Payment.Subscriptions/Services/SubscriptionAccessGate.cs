namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// <see cref="ISubscriptionAccessGate"/> 的内置实现。
/// </summary>
/// <remarks>
/// <para>
/// 判定顺序按代价从低到高：门禁开关 → 账户（解析器）→ 境况（缓存）→ 只有账户<b>确实暂停了</b>才去问豁免
/// （超级管理员、绕过权限码）。绝大多数请求在前三步就放行，不为一个正常账户多做两次权限查询。
/// </para>
/// <para>
/// ★ 超级管理员用的是 <see cref="IFunctionAuthorizationService.IsSuperAdminAsync"/>：与收窄后的超管旁路同一个身份判定。
/// 平台超管被挡在机构业务数据之外，靠的是权限（<c>Authorization:SuperAdminBypassScope = Platform</c>），不是欠费；
/// 他要能在机构停服期间进来收拾账（改价、退款、查付款），所以门禁对他恒放行。
/// </para>
/// <para>
/// 不代表任何账户的用户（Tenant 范围下不属于任何租户的宿主用户）放行：门禁管的是「这个账户欠费了」，
/// 一个跟任何账户都没有计费关系的人无所谓欠不欠。这里放行不是授权，后面的权限检查照常。
/// </para>
/// </remarks>
public class SubscriptionAccessGate : ISubscriptionAccessGate
{
    private readonly IOptionsMonitor<SubscriptionAccessGateOptions> _options;
    private readonly IBillingAccountResolver _accountResolver;
    private readonly ISubscriptionService _subscriptionService;
    private readonly SubscriptionStandingCache _cache;
    private readonly ILogger<SubscriptionAccessGate> _logger;
    private readonly IFunctionAuthorizationService? _functionAuthorization;
    private readonly IPermissionChecker? _permissionChecker;

    /// <summary>DI 构造函数。超管判定与权限检查器都是可选的：没有加载 Authorization 模块时没有超管、也没有绕过码可言。</summary>
    public SubscriptionAccessGate(
        IOptionsMonitor<SubscriptionAccessGateOptions> options,
        IBillingAccountResolver accountResolver,
        ISubscriptionService subscriptionService,
        SubscriptionStandingCache cache,
        ILogger<SubscriptionAccessGate> logger,
        IFunctionAuthorizationService? functionAuthorization = null,
        IPermissionChecker? permissionChecker = null)
    {
        _options = Check.NotNull(options);
        _accountResolver = Check.NotNull(accountResolver);
        _subscriptionService = Check.NotNull(subscriptionService);
        _cache = Check.NotNull(cache);
        _logger = Check.NotNull(logger);
        _functionAuthorization = functionAuthorization;
        _permissionChecker = permissionChecker;
    }

    /// <inheritdoc />
    public async Task<SubscriptionAccessDecision> EvaluateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled)
            return SubscriptionAccessDecision.Pass;

        var access = await _accountResolver.ResolveAsync(userId, cancellationToken);
        if (access == null)
            return SubscriptionAccessDecision.Pass;

        var standing = await GetCachedStandingAsync(access.Account, options, cancellationToken);
        if (standing != BillingStanding.Suspended)
            return new SubscriptionAccessDecision(true, standing, access.CanManage);

        if (await IsExemptAsync(userId, options))
            return new SubscriptionAccessDecision(true, standing, access.CanManage);

        return new SubscriptionAccessDecision(false, standing, access.CanManage);
    }

    /// <inheritdoc />
    public async Task<BillingStandingDto> GetStandingAsync(Guid userId, string? productCode = null, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        var product = string.IsNullOrWhiteSpace(productCode) ? options.ProductCode : productCode;

        var access = await _accountResolver.ResolveAsync(userId, cancellationToken);
        if (access == null)
            return new BillingStandingDto { Standing = BillingStanding.None, GateEnabled = options.Enabled, IsExempt = true };

        var detail = await _subscriptionService.GetStandingDetailAsync(access.Account, product, cancellationToken);

        // 只刷新门禁真正读的那一份（门禁配置的产品）；调用方问的是别的产品时不碰它
        if (string.Equals(product, options.ProductCode, StringComparison.Ordinal))
            await _cache.SetAsync(access.Account, product, detail.Standing, TimeSpan.FromSeconds(options.CacheSeconds), cancellationToken);

        detail.CanView = access.CanView;
        detail.CanManage = access.CanManage;
        detail.GateEnabled = options.Enabled;
        detail.IsExempt = !options.Enabled || await IsExemptAsync(userId, options);

        if (!access.CanView)
            ClearDetails(detail);

        return detail;
    }

    private async Task<BillingStanding> GetCachedStandingAsync(BillingAccountRef account, SubscriptionAccessGateOptions options, CancellationToken cancellationToken)
    {
        if (options.CacheSeconds > 0)
        {
            var cached = await _cache.GetAsync(account, options.ProductCode, cancellationToken);
            if (cached.HasValue)
                return cached.Value;
        }

        var standing = await _subscriptionService.GetStandingAsync(account, options.ProductCode, cancellationToken);
        await _cache.SetAsync(account, options.ProductCode, standing, TimeSpan.FromSeconds(options.CacheSeconds), cancellationToken);
        return standing;
    }

    private async Task<bool> IsExemptAsync(Guid userId, SubscriptionAccessGateOptions options)
    {
        if (_functionAuthorization != null && await _functionAuthorization.IsSuperAdminAsync(userId))
            return true;

        if (options.BypassPermissions is not { Count: > 0 })
            return false;

        if (_permissionChecker == null)
        {
            _logger.LogWarning("Payment:Subscription:AccessGate:BypassPermissions is configured but no permission checker is registered; nobody can bypass the gate. Load the Authorization module.");
            return false;
        }

        foreach (var code in options.BypassPermissions)
        {
            if (await _permissionChecker.IsGrantedAsync(userId, code))
                return true;
        }

        return false;
    }

    private static void ClearDetails(BillingStandingDto dto)
    {
        dto.SubscriptionId = null;
        dto.SubscriptionNo = null;
        dto.PlanName = null;
        dto.Status = null;
        dto.PastDueSince = null;
        dto.GraceDeadline = null;
        dto.SuspendedAt = null;
        dto.AmountDue = null;
        dto.Currency = null;
    }
}
