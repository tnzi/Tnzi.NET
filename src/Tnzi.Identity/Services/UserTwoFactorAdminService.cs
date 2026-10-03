namespace Tnzi.Identity.Services;

/// <inheritdoc cref="IUserTwoFactorAdminService" />
public class UserTwoFactorAdminService : ApplicationService, IUserTwoFactorAdminService
{
    private readonly ITwoFactorService _twoFactor;
    private readonly IUserTenantScopeProvider _scope;
    private readonly IFunctionAuthorizationService? _functionAuthorization;

    public UserTwoFactorAdminService(
        IServiceProvider serviceProvider,
        ITwoFactorService twoFactor,
        IUserTenantScopeProvider scope,
        IFunctionAuthorizationService? functionAuthorization = null)
        : base(serviceProvider)
    {
        _twoFactor = Check.NotNull(twoFactor);
        _scope = Check.NotNull(scope);
        _functionAuthorization = functionAuthorization;
    }

    /// <inheritdoc />
    public async Task<Result<TwoFactorStatusDto>> GetStatusAsync(Guid userId)
    {
        if (!await _scope.ContainsAsync(userId))
        {
            return NotFound<TwoFactorStatusDto>();
        }

        return await _twoFactor.GetTwoFactorStatusAsync(userId);
    }

    /// <inheritdoc />
    public Task<Result> SuspendAsync(Guid userId)
        => InScopeAsync(userId, () => _twoFactor.SuspendTwoFactorAsync(userId), "suspended");

    /// <inheritdoc />
    public Task<Result> ResumeAsync(Guid userId)
        => InScopeAsync(userId, () => _twoFactor.ResumeTwoFactorAsync(userId), "resumed");

    /// <inheritdoc />
    public Task<Result> EnableMethodAsync(Guid userId, TwoFactorType type)
    {
        if (type == TwoFactorType.Totp)
        {
            // ITwoFactorService 自己也会拒绝，但那句话是说给自助用户听的（「走 setup 流程」）。
            // 管理员需要听到的是：这件事不是你能替他做的。
            // Passkey 不在此列：它的登记同样只能持有人自己做，但「把登记好的密钥设成第二因子」只是一个开关，
            // 与「在已验证的地址上启用短信」同一形状 —— 没登记凭据时由 ITwoFactorService 拒绝。
            return Task.FromResult(Fail(
                "An authenticator app can only be enrolled by the account holder from their own device; management can disable one, never enable it.",
                400, ErrorCodes.VALIDATION_ERROR));
        }

        return InScopeAsync(
            userId,
            async () => await _twoFactor.EnableTwoFactorAsync(userId, new EnableTwoFactorDto { Type = type }),
            $"method {type} enabled");
    }

    /// <inheritdoc />
    public Task<Result> DisableMethodAsync(Guid userId, TwoFactorType type)
        => InScopeAsync(userId, () => _twoFactor.DisableTwoFactorMethodAsync(userId, type), $"method {type} disabled");

    /// <inheritdoc />
    public Task<Result> SetPreferredAsync(Guid userId, TwoFactorType type)
        => InScopeAsync(userId, () => _twoFactor.SetPreferredTwoFactorAsync(userId, type), $"preferred set to {type}");

    /// <inheritdoc />
    public Task<Result> ResetAsync(Guid userId)
        => InScopeAsync(userId, () => _twoFactor.DisableTwoFactorAsync(userId), "reset");

    private async Task<Result> InScopeAsync(Guid userId, Func<Task<Result>> action, string what)
    {
        if (!await _scope.ContainsAsync(userId))
        {
            return NotFound();
        }

        if (await SuperAdminTargetGuard.IsForbiddenAsync(_functionAuthorization, CurrentUser?.Id, userId))
        {
            return Fail(SuperAdminTargetGuard.Message, 403, ErrorCodes.FORBIDDEN);
        }

        var result = await action();
        if (result.Succeeded)
        {
            LogInformation("Two-factor {What} for user {UserId} by {Actor}", what, userId, CurrentUser?.Id);
        }

        return result;
    }

    private Result NotFound() => Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);

    private Result<T> NotFound<T>() => Fail<T>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
}
