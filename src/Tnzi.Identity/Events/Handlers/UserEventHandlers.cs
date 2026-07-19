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
    private readonly IUserAgentParserService? _userAgentParser;

    public UserLoggedInEventHandler(
        ILoginLogInternalService? loginLogInternalService = null,
        ISessionService? sessionService = null,
        IUserAgentParserService? userAgentParser = null)
    {
        _loginLogInternalService = loginLogInternalService;
        _sessionService = sessionService;
        _userAgentParser = userAgentParser;
    }

    public async Task HandleAsync(UserLoggedInEvent @event, CancellationToken cancellationToken = default)
    {
        // 不吞异常：登录日志/会话创建是持久化副作用，失败必须冒泡给总线做隔离/重试/DLQ
        // （后台处理器分发在 LocalEventBus 已统一 LogError 观测）。此前的空 catch 会连日志都吞掉。

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

        // 处理会话创建：deviceInfo 从 UserAgent 解析（供会话统计 Top device 聚合）
        if (_sessionService != null)
        {
            await _sessionService.CreateSessionAsync(
                @event.UserId,
                BuildDeviceInfo(@event.UserAgent),
                @event.IpAddress,
                @event.UserAgent);
        }
    }

    /// <summary>
    /// 从 UserAgent 提取简短设备描述（如 "Chrome on Windows"），
    /// 解析器缺失或 UA 不可识别时返回 null（统计端会忽略空值）。
    /// </summary>
    private string? BuildDeviceInfo(string? userAgent)
    {
        if (_userAgentParser == null || string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var info = _userAgentParser.Parse(userAgent);
        if (!string.IsNullOrEmpty(info.Browser) && !string.IsNullOrEmpty(info.OperatingSystem))
        {
            return $"{info.Browser} on {info.OperatingSystem}";
        }

        return info.Browser ?? info.OperatingSystem ?? info.DeviceType;
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
