namespace Tnzi.Authorization.DataAuth.Events.Handlers;

/// <summary>
/// 处理 Identity 发布的 <see cref="UserRolesChangedEvent"/>：作废该用户缓存着的全部数据过滤表达式。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 表达式按「角色集」编译（每个角色的范围 OR 起来），缓存 15 分钟，而缓存键里没有角色集。
/// 用户被移出一个宽范围角色之后，功能级权限缓存立刻被父模块清掉，行级的 OR 表达式却在每个已缓存的实例上
/// 继续生效，被摘掉的那个角色的行照样查得出来。删除角色同样经过这里：Identity 删角色时为每个成员各发一条本事件。
/// </para>
/// <para>
/// 不吞异常：失效失败交给总线的重试与死信，与父模块同名处理器同一约定。
/// </para>
/// </remarks>
public class UserRolesChangedDataFilterCacheHandler : IEventHandler<UserRolesChangedEvent>
{
    private readonly ICache? _cache;
    private readonly ILogger<UserRolesChangedDataFilterCacheHandler> _logger;

    public UserRolesChangedDataFilterCacheHandler(
        ILogger<UserRolesChangedDataFilterCacheHandler> logger,
        ICache? cache = null)
    {
        _logger = Check.NotNull(logger);
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task HandleAsync(UserRolesChangedEvent @event, CancellationToken cancellationToken = default)
    {
        Check.NotNull(@event);

        await DataAuthService.BumpUserVersionAsync(_cache, @event.UserId);
        _logger.LogDebug(
            "Invalidated cached data filters for user {UserId} after a role change ({ChangeType}).",
            @event.UserId,
            @event.ChangeType);
    }
}
