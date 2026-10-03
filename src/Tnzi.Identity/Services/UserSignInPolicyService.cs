namespace Tnzi.Identity.Services;

/// <inheritdoc cref="IUserSignInPolicyService" />
public class UserSignInPolicyService : ApplicationService, IUserSignInPolicyService
{
    private readonly IRepository<UserSignInPolicy, Guid> _policies;
    private readonly UserManager<User> _userManager;
    private readonly IUserTenantScopeProvider _scope;
    private readonly IOptionsMonitor<IdentityOptions> _options;
    private readonly IFunctionAuthorizationService? _functionAuthorization;

    public UserSignInPolicyService(
        IServiceProvider serviceProvider,
        IRepository<UserSignInPolicy, Guid> policies,
        UserManager<User> userManager,
        IUserTenantScopeProvider scope,
        IOptionsMonitor<IdentityOptions> options,
        IFunctionAuthorizationService? functionAuthorization = null)
        : base(serviceProvider)
    {
        _functionAuthorization = functionAuthorization;
        _policies = Check.NotNull(policies);
        _userManager = Check.NotNull(userManager);
        _scope = Check.NotNull(scope);
        _options = Check.NotNull(options);
    }

    /// <inheritdoc />
    public async Task<Result<UserSignInPolicyDto>> GetAsync(Guid userId)
    {
        var user = await FindScopedUserAsync(userId);
        if (user == null)
        {
            return Fail<UserSignInPolicyDto>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        var policy = await _policies.AsQueryable().FirstOrDefaultAsync(p => p.UserId == userId);
        return Ok(await ToDtoAsync(user, policy));
    }

    /// <inheritdoc />
    public async Task<Result<UserSignInPolicyDto>> SetIpAllowListAsync(Guid userId, SetIpAllowListDto input)
    {
        Check.NotNull(input);

        var user = await FindScopedUserAsync(userId);
        if (user == null)
        {
            return Fail<UserSignInPolicyDto>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        if (await SuperAdminTargetGuard.IsForbiddenAsync(_functionAuthorization, CurrentUser?.Id, userId))
        {
            return Fail<UserSignInPolicyDto>(SuperAdminTargetGuard.Message, 403, ErrorCodes.FORBIDDEN);
        }

        var invalid = SignInIpAllowList.FindInvalidEntries(input.AllowedIps);
        if (invalid.Count > 0)
        {
            return Fail<UserSignInPolicyDto>(
                $"Invalid IP allow-list entries: {string.Join(", ", invalid)}. Use exact IPv4/IPv6 addresses or CIDR ranges, one per line.",
                400, ErrorCodes.VALIDATION_ERROR);
        }

        var entries = SignInIpAllowList.ParseEntries(input.AllowedIps);
        if (input.Enabled && entries.Count == 0)
        {
            // 守卫把空列表读成「不限制」，所以一个开着开关的空列表什么也不保护，
            // 却会在界面上显示为已开启。拒绝存它，而不是存一个谎。
            return Fail<UserSignInPolicyDto>(
                "Enabling the IP allow-list requires at least one address or range.",
                400, ErrorCodes.VALIDATION_ERROR);
        }

        var policy = await _policies.AsQueryable(withTracking: true).FirstOrDefaultAsync(p => p.UserId == userId);
        var text = string.IsNullOrWhiteSpace(input.AllowedIps) ? null : input.AllowedIps;

        if (!input.Enabled && text == null)
        {
            // 「关了、清空了」就是「没有限制」，而没有限制的规范形态是没有行。
            if (policy != null)
            {
                await _policies.DeleteAsync(policy);
                LogInformation("Sign-in IP allow-list removed for user {UserId} by {Actor}", userId, CurrentUser?.Id);
            }

            return Ok(await ToDtoAsync(user, null));
        }

        if (policy == null)
        {
            policy = await InsertOrOverwriteAsync(userId, input.Enabled, text);
        }
        else
        {
            policy.IpAllowListEnabled = input.Enabled;
            policy.AllowedIps = text;
            await _policies.UpdateAsync(policy);
        }

        LogInformation(
            "Sign-in IP allow-list for user {UserId} set to {State} with {Count} entries by {Actor}",
            userId, input.Enabled ? "enabled" : "disabled", entries.Count, CurrentUser?.Id);

        return Ok(await ToDtoAsync(user, policy));
    }

    /// <summary>
    /// 首次写入。两个管理员同时给同一个账号写第一份策略时，两边都读到「没有行」、都去插，
    /// 后到的那个撞上 <c>UserId</c> 唯一索引 —— 那不是错误，只是晚了一步：丢弃失败的实体、读回先到的那一行、
    /// 按本次请求改写它（与「已有行时改写」同一语义，后写者生效），而不是把唯一索引冲突当 500 抛给操作员。
    /// </summary>
    private async Task<UserSignInPolicy> InsertOrOverwriteAsync(Guid userId, bool enabled, string? text)
    {
        var policy = new UserSignInPolicy
        {
            UserId = userId,
            IpAllowListEnabled = enabled,
            AllowedIps = text
        };

        try
        {
            await _policies.InsertAsync(policy);
            return policy;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            // 失败的实体仍在变更跟踪器里，不丢掉它，本作用域下一次 SaveChanges 会把它再插一遍。
            _policies.Discard(policy);
        }

        var existing = await _policies.AsQueryable(withTracking: true).FirstOrDefaultAsync(p => p.UserId == userId)
            ?? throw new InvalidOperationException($"The sign-in policy of user {userId} collided on insert but could not be read back.");
        existing.IpAllowListEnabled = enabled;
        existing.AllowedIps = text;
        await _policies.UpdateAsync(existing);
        return existing;
    }

    private async Task<User?> FindScopedUserAsync(Guid userId)
    {
        if (!await _scope.ContainsAsync(userId))
        {
            return null;
        }

        return await _userManager.FindByGuidAsync(userId);
    }

    private async Task<UserSignInPolicyDto> ToDtoAsync(User user, UserSignInPolicy? policy)
    {
        var exemptRoles = _options.CurrentValue.AccountSecurity.IpAllowListExemptRoles;
        var exemptedBy = new List<string>();
        if (exemptRoles.Length > 0)
        {
            var roles = await _userManager.GetRolesAsync(user) ?? [];
            exemptedBy = roles.Intersect(exemptRoles, StringComparer.OrdinalIgnoreCase).ToList();
        }

        return new UserSignInPolicyDto
        {
            UserId = user.Id,
            IpAllowListEnabled = policy?.IpAllowListEnabled ?? false,
            AllowedIps = policy?.AllowedIps,
            Entries = SignInIpAllowList.ParseEntries(policy?.AllowedIps).ToList(),
            ExemptedByRoles = exemptedBy,
            CallerIpAddress = ScopedContext?.ClientIpAddress
        };
    }
}
