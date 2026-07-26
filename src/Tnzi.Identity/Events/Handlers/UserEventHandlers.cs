using LoginStatus = Tnzi.Identity.Entities.LoginStatus;

namespace Tnzi.Identity.Events.Handlers;

/// <summary>
/// 用户登录事件处理器 —— 只负责登录日志记录（辅助副作用）。
/// 会话创建已移至同步路径（<see cref="Tnzi.Identity.Services.ILoginSessionCoordinator"/>），
/// 因为会话ID要在**签发令牌之前**拿到并写入 access token 的 session_id claim、绑定刷新令牌；
/// 事件处理器是异步后台执行，无法满足这一时序（且会与多登录策略判定产生竞态）。
/// Runs as background handler: gets an independent scope (and independent DbContext)
/// via Task.Run so the login-log DB write does not race with the request-scope DbContext.
/// </summary>
[BackgroundEventHandler]
public class UserLoggedInEventHandler : IEventHandler<UserLoggedInEvent>
{
    private readonly ILoginLogInternalService? _loginLogInternalService;

    public UserLoggedInEventHandler(ILoginLogInternalService? loginLogInternalService = null)
    {
        _loginLogInternalService = loginLogInternalService;
    }

    public async Task HandleAsync(UserLoggedInEvent @event, CancellationToken cancellationToken = default)
    {
        // 不吞异常：登录日志是持久化副作用，失败必须冒泡给总线做隔离/重试/DLQ
        // （后台处理器分发在 LocalEventBus 已统一 LogError 观测）。此前的空 catch 会连日志都吞掉。
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
        // 占位：会话撤销和 Token 清理已在服务层完成（核心业务逻辑）
        // 未来若加日志/统计等副作用，不要用 log-only try/catch 吞异常，让其冒泡给总线。
        await Task.CompletedTask;
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
        // 不吞异常：失败登录日志是持久化副作用，写入失败必须冒泡给总线（后台分发已 LogError）。
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
}
