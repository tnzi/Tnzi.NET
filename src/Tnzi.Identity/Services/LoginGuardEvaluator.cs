namespace Tnzi.Identity.Services;

/// <inheritdoc cref="ILoginGuardEvaluator" />
public class LoginGuardEvaluator : ILoginGuardEvaluator
{
    private readonly IReadOnlyList<ILoginGuard> _guards;
    private readonly ILogger<LoginGuardEvaluator> _logger;

    public LoginGuardEvaluator(IEnumerable<ILoginGuard> guards, ILogger<LoginGuardEvaluator> logger)
    {
        _guards = Check.NotNull(guards).OrderBy(g => g.Order).ToList();
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public bool HasGuards => _guards.Count > 0;

    /// <inheritdoc />
    public async Task<LoginGuardResult> EvaluateAsync(LoginGuardContext context, CancellationToken cancellationToken = default)
    {
        Check.NotNull(context);

        foreach (var guard in _guards)
        {
            LoginGuardResult result;
            try
            {
                result = await guard.EvaluateAsync(context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Fail-closed：一条准入策略静默失效（比如白名单服务挂了就人人都能进）
                // 远比一次登录失败危险。拒绝时对外仍与凭据错误同形。
                _logger.LogError(ex, "Login guard {Guard} threw for user {UserId}; denying the login.",
                    guard.GetType().Name, context.User.Id);
                return LoginGuardResult.DenyAsInvalidCredentials(
                    $"Login guard {guard.GetType().Name} failed to evaluate");
            }

            if (!result.Allowed)
            {
                _logger.LogInformation(
                    "Login guard {Guard} denied user {UserId} ({Method}) from {Ip}: {Reason}",
                    guard.GetType().Name, context.User.Id, context.Method, context.IpAddress, result.AuditReason);
                return result;
            }
        }

        return LoginGuardResult.Allow();
    }
}
