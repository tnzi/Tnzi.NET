namespace Tnzi.Authorization.DataAuth.Extensions;

/// <summary>
/// <see cref="RepositoryExtensions.WithDataAuthAsync{TEntity,TKey}"/> 在<b>没有当前用户</b>时的处置。
/// </summary>
/// <remarks>
/// 「有用户但没有规则」= 不限制，是本模块记录在案的设计；「没有用户」不是同一件事。
/// 行级过滤的全部输入是「这个用户有哪些角色」，没有用户就没有任何东西可判 ——
/// 这时放行等于一条安静的 fail-open：调用它的匿名端点拿到全表，而日志、返回值、界面全都正常。
/// 因此默认拒绝；真的要在匿名端点上用同一份查询（例如「公开行 + 登录后按规则过滤」的混合端点），
/// 由调用方<b>显式</b>选 <see cref="Unrestricted"/>，把这个决定写在调用点上。
/// </remarks>
public enum AnonymousDataAccess
{
    /// <summary>没有用户时不返回任何行（默认）。</summary>
    Deny = 0,

    /// <summary>没有用户时返回未过滤的查询 —— 由调用方为这个端点的匿名可见性负责。</summary>
    Unrestricted = 1,
}

/// <summary>
/// 仓储扩展方法（用于数据授权）
/// </summary>
public static class RepositoryExtensions
{
    /// <summary>
    /// 应用数据权限过滤的查询
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    /// <typeparam name="TKey">主键类型</typeparam>
    /// <param name="repository">仓储</param>
    /// <param name="dataAuthService">数据授权服务</param>
    /// <param name="currentUser">当前用户</param>
    /// <param name="operation">操作类型</param>
    /// <param name="anonymousAccess">
    /// 没有当前用户时的处置，默认 <see cref="AnonymousDataAccess.Deny"/>（不返回任何行）。
    /// 见 <see cref="AnonymousDataAccess"/>。
    /// </param>
    /// <returns>应用了数据权限过滤的查询</returns>
    public static async Task<IQueryable<TEntity>> WithDataAuthAsync<TEntity, TKey>(
        this IRepository<TEntity, TKey> repository,
        IDataAuthService dataAuthService,
        ICurrentUser currentUser,
        DataAuthOperation operation = DataAuthOperation.Query,
        AnonymousDataAccess anonymousAccess = AnonymousDataAccess.Deny)
        where TEntity : class, Tnzi.Domain.Entities.IEntity<TKey>
        where TKey : notnull
    {
        Check.NotNull(repository);
        Check.NotNull(dataAuthService);
        Check.NotNull(currentUser);

        var query = repository.AsQueryable();

        var userId = currentUser.Id;
        if (userId == null || userId == Guid.Empty)
        {
            // 没有用户就没有角色可查，服务不该被问到。默认 fail-closed；
            // 放行必须是调用点上的一个显式决定，而不是这个方法替它做的。
            return anonymousAccess == AnonymousDataAccess.Unrestricted
                ? query
                : query.Where(_ => false);
        }

        var filter = await dataAuthService.GetDataFilterAsync<TEntity>(userId.Value, operation);
        if (filter != null)
        {
            query = query.Where(filter);
        }

        return query;
    }
}
