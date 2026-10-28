namespace Tnzi.Authorization.Events.Handlers;

/// <summary>
/// 处理 Identity 模块发布的 <see cref="RoleDeletedEvent"/>：删掉被删角色名下的授权行（<see cref="RoleFunction"/>）。
/// </summary>
/// <remarks>
/// <para>
/// ★ <c>RoleFunction.RoleId</c> 是一个裸 Guid，没有指向角色表的外键，所以角色被删之后它的授权行原样留着。
/// 它们不再授予任何人什么（成员关系已随角色级联删除），但会一直挡住「删除功能」：那条守卫跨租户数
/// 「还有没有角色挂着这个码」，而这些孤儿行的角色已经不存在，界面上也找不到任何入口把它们摘掉。
/// </para>
/// <para>
/// 成员的权限缓存不归这里管：Identity 删除角色时为每个成员发 <see cref="UserRolesChangedEvent"/>，
/// 由 <see cref="UserRolesChangedEventHandler"/> 按人失效。
/// 不吞异常：清理失败交给总线的重试与死信，与同目录的处理器同一约定。
/// </para>
/// </remarks>
public class RoleDeletedEventHandler : IEventHandler<RoleDeletedEvent>
{
    private readonly IRepository<RoleFunction, Guid> _roleFunctionRepository;
    private readonly ILogger<RoleDeletedEventHandler> _logger;

    public RoleDeletedEventHandler(
        IRepository<RoleFunction, Guid> roleFunctionRepository,
        ILogger<RoleDeletedEventHandler> logger)
    {
        _roleFunctionRepository = Check.NotNull(roleFunctionRepository);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task HandleAsync(RoleDeletedEvent @event, CancellationToken cancellationToken = default)
    {
        Check.NotNull(@event);

        await _roleFunctionRepository.DeleteAsync(rf => rf.RoleId == @event.RoleId, cancellationToken);
        _logger.LogDebug("Removed the permission grants of deleted role {RoleId}.", @event.RoleId);
    }
}
