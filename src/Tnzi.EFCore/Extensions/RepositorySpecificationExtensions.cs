
namespace Tnzi.EFCore.Extensions;

/// <summary>
/// Repository规范扩展方法
/// </summary>
public static class RepositorySpecificationExtensions
{
    /// <summary>
    /// 根据规范获取实体列表
    /// </summary>
    public static async Task<List<TEntity>> GetListAsync<TEntity>(
        this IRepository<TEntity> repository,
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
        where TEntity : class, IEntity
    {
        var expression = specification.ToExpression();
        return await repository.ToListAsync(expression, cancellationToken);
    }

    /// <summary>
    /// 根据规范获取分页列表
    /// </summary>
    public static async Task<IPagedList<TEntity>> GetPagedListAsync<TEntity>(
        this IRepository<TEntity> repository,
        ISpecification<TEntity> specification,
        PagedQuery query,
        CancellationToken cancellationToken = default)
        where TEntity : class, IEntity
    {
        var expression = specification.ToExpression();
        return await repository.GetPagedListAsync(expression, query, cancellationToken);
    }

    /// <summary>
    /// 根据规范获取数量
    /// </summary>
    public static async Task<int> CountAsync<TEntity>(
        this IRepository<TEntity> repository,
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
        this IRepository<TEntity> repository,
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
        this IRepository<TEntity> repository,
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
        where TEntity : class, IEntity
    {
        var expression = specification.ToExpression();
        return await repository.FirstOrDefaultAsync(expression, cancellationToken);
    }

    /// <summary>
    /// 根据规范获取单个实体
    /// </summary>
    public static async Task<TEntity?> SingleOrDefaultAsync<TEntity>(
        this IRepository<TEntity> repository,
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
        where TEntity : class, IEntity
    {
        var expression = specification.ToExpression();
        return await repository.SingleOrDefaultAsync(expression, cancellationToken);
    }
}