using Tnzi.Domain.Repositories;
using Tnzi.Domain.Specifications;

namespace Tnzi.Data;

/// <summary>
/// <see cref="IDataScopeProvider{TEntity}"/> 的组合与应用扩展：把容器里注册的所有同实体范围 provider
/// 的谓词以 AND 合成，并应用到 <see cref="IQueryable{T}"/> 或用于单行访问校验。
/// </summary>
[StableApi(Since = "0.1.0")]
public static class DataScopeExtensions
{
    /// <summary>
    /// 把多个 provider 的非空 <see cref="IDataScopeProvider{TEntity}.GetFilter"/> 谓词以 AND 合成为单个谓词。
    /// 全部为空/无 provider 时返回 <c>null</c>（表示不施加限制）。
    /// </summary>
    public static Expression<Func<TEntity, bool>>? BuildDataScopeFilter<TEntity>(
        IEnumerable<IDataScopeProvider<TEntity>> providers)
        where TEntity : class
    {
        Check.NotNull(providers);

        Expression<Func<TEntity, bool>>? combined = null;
        foreach (var provider in providers)
        {
            var filter = provider.GetFilter();
            if (filter == null)
                continue;

            combined = combined == null ? filter : And(combined, filter);
        }

        return combined;
    }

    /// <summary>
    /// 应用给定 provider 集合的合成范围谓词到查询。无有效谓词时原样返回。
    /// </summary>
    public static IQueryable<TEntity> ApplyDataScope<TEntity>(
        this IQueryable<TEntity> source,
        IEnumerable<IDataScopeProvider<TEntity>> providers)
        where TEntity : class
    {
        Check.NotNull(source);

        var filter = BuildDataScopeFilter(providers);
        return filter == null ? source : source.Where(filter);
    }

    /// <summary>
    /// 从容器解析所有 <see cref="IDataScopeProvider{TEntity}"/> 并应用其合成范围谓词到查询。
    /// 未注册任何 provider 时原样返回（零影响）。
    /// </summary>
    public static IQueryable<TEntity> ApplyDataScope<TEntity>(
        this IQueryable<TEntity> source,
        IServiceProvider serviceProvider)
        where TEntity : class
    {
        Check.NotNull(serviceProvider);
        return source.ApplyDataScope(serviceProvider.GetServices<IDataScopeProvider<TEntity>>());
    }

    /// <summary>
    /// 从容器解析范围谓词并合成（供服务层把范围 AND 进自己的查询/校验，例如
    /// <see cref="Tnzi.Application.CrudAppService{TEntity,TKey,TDto,TCreateDto,TUpdateDto}"/> 的 scope 钩子）。
    /// </summary>
    public static Expression<Func<TEntity, bool>>? BuildDataScopeFilter<TEntity>(
        this IServiceProvider serviceProvider)
        where TEntity : class
    {
        Check.NotNull(serviceProvider);
        return BuildDataScopeFilter(serviceProvider.GetServices<IDataScopeProvider<TEntity>>());
    }

    /// <summary>
    /// 校验当前主体能否访问指定实体：容器里所有同实体 provider 全部放行才算可访问（AND）。
    /// 未注册任何 provider 时恒为 <c>true</c>。
    /// </summary>
    public static async Task<bool> CanAccessAsync<TEntity>(
        this IServiceProvider serviceProvider,
        TEntity entity,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        Check.NotNull(serviceProvider);
        Check.NotNull(entity);

        foreach (var provider in serviceProvider.GetServices<IDataScopeProvider<TEntity>>())
        {
            if (!await provider.CanAccessAsync(entity, cancellationToken))
                return false;
        }

        return true;
    }

    /// <summary>
    /// 按主键校验访问权限：先经仓储加载实体（不存在=不可访问），再走
    /// <see cref="CanAccessAsync{TEntity}(IServiceProvider, TEntity, CancellationToken)"/> 逐 provider AND 校验。
    /// </summary>
    /// <remarks>
    /// 刻意与 <see cref="CanAccessAsync{TEntity}(IServiceProvider, TEntity, CancellationToken)"/> 分开命名：
    /// 两者都带可选的 <c>cancellationToken</c>，同名重载会让日后给任一方加可选参数变成二进制破坏（RS0026）。
    /// </remarks>
    public static async Task<bool> CanAccessByIdAsync<TEntity, TKey>(
        this IReadOnlyRepository<TEntity, TKey> repository,
        IServiceProvider serviceProvider,
        TKey id,
        CancellationToken cancellationToken = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        Check.NotNull(repository);
        Check.NotNull(serviceProvider);

        var entity = await repository.GetAsync(id, cancellationToken);
        return entity != null && await serviceProvider.CanAccessAsync(entity, cancellationToken);
    }

    private static Expression<Func<TEntity, bool>> And<TEntity>(
        Expression<Func<TEntity, bool>> left,
        Expression<Func<TEntity, bool>> right)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "x");
        var leftBody = ParameterReplacer.Replace(left.Body, left.Parameters[0], parameter);
        var rightBody = ParameterReplacer.Replace(right.Body, right.Parameters[0], parameter);
        return Expression.Lambda<Func<TEntity, bool>>(Expression.AndAlso(leftBody, rightBody), parameter);
    }
}
