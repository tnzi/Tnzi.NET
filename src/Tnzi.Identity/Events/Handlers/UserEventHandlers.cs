using LoginStatus = Tnzi.Identity.Entities.LoginStatus;

namespace Tnzi.Identity.Events.Handlers;

/// <summary>
/// 用户登录事件处理器
/// 处理登录日志记录、会话创建等辅助操作
/// Runs as background handler: gets an independent scope (and independent DbContext)
/// via Task.Run so DB writes (SessionService.CreateSessionAsync etc.) do not race
/// with the request-scope DbContext that is still busy completing the login response.
/// </summary>
[BackgroundEventHandler]
public class UserLoggedInEventHandler : IEventHandler<UserLoggedInEvent>
{
    private readonly ILoginLogInternalService? _loginLogInternalService;
    private readonly ISessionService? _sessionService;

    public UserLoggedInEventHandler(
        ILoginLogInternalService? loginLogInternalService = null,
        ISessionService? sessionService = null)
    {
        _loginLogInternalService = loginLogInternalService;
        _sessionService = sessionService;
    }

    public async Task HandleAsync(UserLoggedInEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            // 处理登录日志记录
            if (_loginLogInternalService != null)
            {
                await _loginLogInternalService.LogAsync(
                    @event.UserId,
                    @event.UserName,
                    @event.IpAddress,
                    @event.UserAgent,
                    LoginStatus.Success,
                    null);
            }

            // 处理会话创建
            if (_sessionService != null)
            {
                await _sessionService.CreateSessionAsync(
                    @event.UserId,
                    null, // deviceInfo — 登录事件不携带设备信息
                    @event.IpAddress,
                    @event.UserAgent);
            }

        }
        catch (Exception)
        {
            // 事件处理器错误不影响主业务流程，静默忽略（框架已有错误隔离，此处为双重保险）
        }
    }
}

/// <summary>
/// 用户登出事件处理器
/// 处理登出后的日志记录、统计等辅助操作
/// 注意：会话撤销和 Token 清理已在服务层完成（核心业务逻辑）
/// </summary>
public class UserLoggedOutEventHandler : IEventHandler<UserLoggedOutEvent>
{
    public async Task HandleAsync(UserLoggedOutEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            // 可以用于日志记录、统计等辅助操作
            // 会话撤销和 Token 清理已在服务层完成（核心业务逻辑）
            await Task.CompletedTask;
        }
        catch (Exception)
        {
            // 事件处理器错误不影响主业务流程，静默忽略
        }
    }
}

/// <summary>
/// 用户登录失败事件处理器
/// 处理失败登录日志记录
/// Same rationale as <see cref="UserLoggedInEventHandler"/>: background scope isolates
/// the login-log DB write from the request-scope DbContext.
/// </summary>
[BackgroundEventHandler]
public class UserLoginFailedEventHandler : IEventHandler<UserLoginFailedEvent>
{
    private readonly ILoginLogInternalService? _loginLogInternalService;

    public UserLoginFailedEventHandler(ILoginLogInternalService? loginLogInternalService = null)
    {
        _loginLogInternalService = loginLogInternalService;
    }

    public async Task HandleAsync(UserLoginFailedEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            // 处理失败登录日志记录
            if (_loginLogInternalService != null)
            {
                await _loginLogInternalService.LogAsync(
                    @event.UserId,
                    @event.UserName,
                    @event.IpAddress,
                    @event.UserAgent,
                    LoginStatus.Failed,
                    @event.FailureReason);
            }
        }
        catch (Exception)
        {
            // 事件处理器错误不影响主业务流程，静默忽略
        }
    }
}
