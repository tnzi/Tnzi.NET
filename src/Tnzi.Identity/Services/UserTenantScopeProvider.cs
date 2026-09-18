namespace Tnzi.Identity.Services;

/// <inheritdoc cref="IUserTenantScopeProvider"/>
public class UserTenantScopeProvider : IUserTenantScopeProvider
{
    private readonly IRepository<User, Guid> _users;
    private readonly ICurrentTenant? _currentTenant;
    private readonly ICurrentUser? _currentUser;
    private readonly bool _multiTenancyEnabled;
    private readonly ILogger<UserTenantScopeProvider>? _logger;

    public UserTenantScopeProvider(
        IRepository<User, Guid> users,
        ICurrentTenant? currentTenant = null,
        ICurrentUser? currentUser = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null,
        ILogger<UserTenantScopeProvider>? logger = null)
    {
        _users = Check.NotNull(users);
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
        _logger = logger;
    }

    /// <inheritdoc />
    public UserTenantScope Current => UserTenantScope.Resolve(_multiTenancyEnabled, _currentTenant, _currentUser);

    /// <inheritdoc />
    public async Task<bool> ContainsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var scope = Current;
        if (scope.IsUnrestricted || userId == scope.SelfUserId)
        {
            return true;
        }

        // 软删的账号也要判：它的会话与登录记录还在，仍归原租户的管理员管；
        // 按过滤器隐掉会让「同租户、已删除」与「别家租户」长得一样。
        var owner = await _users.AsQueryable()
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => new { u.TenantId })
            .FirstOrDefaultAsync(cancellationToken);

        if (owner == null)
        {
            return false;
        }

        if (scope.Contains(userId, owner.TenantId))
        {
            return true;
        }

        // 一次真实的越权尝试不该长得和一次 404 一模一样。
        _logger?.LogWarning(
            "Rejected a cross-tenant user access: user {UserId} belongs to tenant {UserTenantId} but the request is scoped to tenant {TenantId}.",
            userId, owner.TenantId, scope.TenantId);
        return false;
    }

    /// <inheritdoc />
    public IQueryable<Guid>? InScopeUserIds()
    {
        var scope = Current;
        if (scope.IsUnrestricted)
        {
            return null;
        }

        return scope.Apply(_users.AsQueryable().IgnoreQueryFilters()).Select(u => u.Id);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Guid>> FilterAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        Check.NotNull(userIds);

        var distinct = userIds.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return distinct;
        }

        var scope = Current;
        if (scope.IsUnrestricted)
        {
            return distinct;
        }

        // 同 ContainsAsync：软删的账号照常按租户判断，别按过滤器把它们与别家租户混成一样。
        return await scope.Apply(_users.AsQueryable().IgnoreQueryFilters())
            .Where(u => distinct.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
    }
}
