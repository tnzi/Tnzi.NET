namespace Tnzi.Authorization.Extensions;

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
    /// <returns>应用了数据权限过滤的查询</returns>
    public static async Task<IQueryable<TEntity>> WithDataAuthAsync<TEntity, TKey>(
        this IRepository<TEntity, TKey> repository,
        IDataAuthService dataAuthService,
        ICurrentUser currentUser,
        DataAuthOperation operation = DataAuthOperation.Query)
        where TEntity : class, Tnzi.Domain.Entities.IEntity<TKey>
        where TKey : notnull
    {
        var query = repository.AsQueryable();

        if (currentUser.Id == null)
            return query;

        var filter = await dataAuthService.GetDataFilterAsync<TEntity>(currentUser.Id.Value, operation);
        if (filter != null)
        {
            query = query.Where(filter);
        }

        return query;
    }

}

