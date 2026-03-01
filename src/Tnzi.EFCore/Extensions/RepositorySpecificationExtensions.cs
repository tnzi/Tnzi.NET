
namespace Tnzi.EFCore.Extensions;

/// <summary>
/// Repository 规范扩展方法
/// 支持通过 Specification 进行查询，自动应用 Where/OrderBy/Include/Paging
/// </summary>
public static class RepositorySpecificationExtensions
{
    /// <summary>
    /// 根据规范获取实体列表
    /// </summary>
    public static async Task<List<TEntity>> GetListAsync<TEntity>(
        this IReadOnlyRepository<TEntity> repository,
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
        where TEntity : class, IEntity
    {
        var query = ApplySpecification(repository, specification);
        return await query.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 根据规范获取分页列表
    /// </summary>
    public static async Task<IPagedList<TEntity>> GetPagedListAsync<TEntity>(
        this IReadOnlyRepository<TEntity> repository,
        ISpecification<TEntity> specification,
        PagedQuery pagedQuery,
        CancellationToken cancellationToken = default)
        where TEntity : class, IEntity
    {
        var expression = specification.ToExpression();
        return await repository.GetPagedListAsync(expression, pagedQuery, cancellationToken);
    }

    /// <summary>
    /// 根据规范获取数量
    /// </summary>
    public static async Task<int> CountAsync<TEntity>(
        this IReadOnlyRepository<TEntity> repository,
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
        where TEntity : class, IEntity
    {
        var expression = specification.ToExpression();
        return await repository.CountAsync(expression, cancellationToken);
    }

    /// <summary>
    /// 根据规范检查是否存在
    /// </summary>
    public static async Task<bool> AnyAsync<TEntity>(
        this IReadOnlyRepository<TEntity> repository,
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
        where TEntity : class, IEntity
    {
        var expression = specification.ToExpression();
        return await repository.AnyAsync(expression, cancellationToken);
    }

    /// <summary>
    /// 根据规范获取第一个实体
    /// </summary>
    public static async Task<TEntity?> FirstOrDefaultAsync<TEntity>(
        this IReadOnlyRepository<TEntity> repository,
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
        where TEntity : class, IEntity
    {
        var query = ApplySpecification(repository, specification);
        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// 根据规范获取单个实体
    /// </summary>
    public static async Task<TEntity?> SingleOrDefaultAsync<TEntity>(
        this IReadOnlyRepository<TEntity> repository,
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
        where TEntity : class, IEntity
    {
        var query = ApplySpecification(repository, specification);
        return await query.SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// 将 Specification 的 Where/Include/OrderBy/Paging 应用到 IQueryable
    /// </summary>
    private static IQueryable<TEntity> ApplySpecification<TEntity>(
        IReadOnlyRepository<TEntity> repository,
        ISpecification<TEntity> specification)
        where TEntity : class, IEntity
    {
        IQueryable<TEntity> query = repository.AsQueryable();

        // 1. Apply Include
        var includes = specification.IncludeExpressions;
        if (includes.Count > 0)
        {
            foreach (var include in includes)
            {
                query = query.Include(include);
            }
        }

        // 2. Apply Where
        query = query.Where(specification.ToExpression());

        // 3. Apply OrderBy
        var orderBys = specification.OrderByExpressions;
        if (orderBys.Count > 0)
        {
            IOrderedQueryable<TEntity>? orderedQuery = null;
            for (var i = 0; i < orderBys.Count; i++)
            {
                var orderBy = orderBys[i];
                if (i == 0)
                {
                    orderedQuery = orderBy.Descending
                        ? query.OrderByDescending(orderBy.KeySelector)
                        : query.OrderBy(orderBy.KeySelector);
                }
                else
                {
                    orderedQuery = orderBy.Descending
                        ? orderedQuery!.ThenByDescending(orderBy.KeySelector)
                        : orderedQuery!.ThenBy(orderBy.KeySelector);
                }
            }

            query = orderedQuery!;
        }

        // 4. Apply Paging
        var paging = specification.Paging;
        if (paging.HasValue)
        {
            query = query.Skip(paging.Value.Skip).Take(paging.Value.Take);
        }

        return query;
    }
}
