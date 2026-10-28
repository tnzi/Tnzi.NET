namespace Tnzi.SignalR.Filters;

/// <summary>
/// 实时连接管理端只对宿主开放：多租户开启时，被钉在某个租户上的调用者一律 403。
/// </summary>
/// <remarks>
/// <para>
/// ★ 连接登记（<see cref="IConnectionManager"/>）是进程级的：一个实例上所有租户的连接都在同一张表里，
/// 元数据（用户名、来源地址、User-Agent）也没有租户维度。管理端点全量读这张表、按任意用户 ID 断开连接，
/// 于是一个被授予了 <c>system.signalr.*</c> 的租户管理员能列出<b>所有租户</b>在线用户的用户名、地址与 UA，
/// 并强制断开别的租户的用户（断开是真断开，客户端不会自动重连）。
/// </para>
/// <para>
/// 与外部 CLI 运行时同一口径：宿主级资源只由宿主管理，租户答 403。判定与全局过滤器同源：
/// <see cref="ICurrentTenant"/> 优先，退回当前用户的租户 claim；多租户关闭时恒为宿主，单租户部署行为不变。
/// </para>
/// <para>
/// 以过滤器挂在控制器类上而不是写在动作里：动作是 <c>virtual</c> 的，消费方继承覆写某个动作时，
/// 写在动作里的判定会跟着被覆写掉；类级过滤器随继承保留。
/// </para>
/// </remarks>
public sealed class HostContextOnlyFilter : IAsyncActionFilter
{
    /// <summary>租户调用者收到的消息。</summary>
    public const string HostManagedMessage =
        "Realtime connection management is a host-level surface: connections from every tenant share this registry. "
        + "Only the host can view or disconnect them.";

    private readonly ICurrentTenant? _currentTenant;
    private readonly ICurrentUser? _currentUser;
    private readonly IOptions<MultiTenancyOptions>? _multiTenancyOptions;

    /// <summary>初始化过滤器（经 <c>TypeFilter</c> 按请求创建）。</summary>
    public HostContextOnlyFilter(
        ICurrentTenant? currentTenant = null,
        ICurrentUser? currentUser = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
    {
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _multiTenancyOptions = multiTenancyOptions;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        Check.NotNull(context);
        Check.NotNull(next);

        if (IsTenantCaller())
        {
            context.Result = new ObjectResult(ApiResult.Error(HostManagedMessage, 403, SignalRErrorCodes.HostManaged))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }

    private bool IsTenantCaller()
        => (_multiTenancyOptions?.Value.Enabled ?? false)
           && (_currentTenant?.Id ?? _currentUser?.TenantId) is not null;
}
