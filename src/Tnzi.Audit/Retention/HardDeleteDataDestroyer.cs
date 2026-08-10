using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tnzi.Audit.Retention;

/// <summary>
/// 默认销毁器：把到期记录从库里<strong>真正删掉</strong>。
/// </summary>
/// <remarks>
/// <para>
/// <strong>为什么不走仓储的 <c>DeleteAsync</c>。</strong>仓储对实现了 <c>ISoftDelete</c> 的实体
/// 执行的是软删除——把 <c>IsDeleted</c> 置真。那只是让数据在应用里看不见，
/// 行还在库里、也还在每一份备份里。合规意义上的销毁不能这么做，
/// 因此本实现显式走 <c>ExecuteDelete</c> 这条绕过软删除转换的路径。
/// </para>
/// <para>
/// <strong>连已经软删除的行一并销毁</strong>（<c>IgnoreQueryFilters</c>）：
/// 那些行同样占着库、同样在备份里，保留期对它们一视同仁。
/// 只看得见未软删的记录，等于把「用户删过一次」的数据永久留下。
/// </para>
/// <para>
/// <strong>只支持单一主键的实体。</strong>复合主键的实体请注册自己的
/// <see cref="IDataDestroyer"/>——与其猜一个可能删错行的通用实现，
/// 不如明确地拒绝并让消费方写出自己的删除条件。
/// </para>
/// </remarks>
public class HardDeleteDataDestroyer : IDataDestroyer
{
    private static readonly MethodInfo EfPropertyMethod =
        typeof(EF).GetMethod(nameof(EF.Property))!;

    private static readonly MethodInfo ContainsMethod =
        typeof(Enumerable).GetMethods()
            .Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2);

    private readonly IServiceProvider _serviceProvider;
    private readonly IEntityManager _entityManager;

    /// <summary>
    /// 初始化 <see cref="HardDeleteDataDestroyer"/>。
    /// </summary>
    public HardDeleteDataDestroyer(IServiceProvider serviceProvider, IEntityManager entityManager)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _entityManager = Check.NotNull(entityManager);
    }

    /// <inheritdoc />
    public string Mode => "hard-delete";

    /// <inheritdoc />
    public async Task<int> DestroyAsync<TEntity>(
        IReadOnlyList<TEntity> entities,
        CancellationToken cancellationToken = default)
        where TEntity : class, IEntity
    {
        Check.NotNull(entities);

        if (entities.Count == 0)
        {
            return 0;
        }

        var dbContext = ResolveDbContext<TEntity>();
        var efEntityType = dbContext.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).FullName}' is not mapped in DbContext "
                + $"'{dbContext.GetType().Name}'. A retention policy can only target mapped entities.");

        var predicate = BuildPrimaryKeyPredicate<TEntity>(efEntityType, entities);

        // ExecuteDelete 是数据库端裸 SQL，不经 SaveChanges——这既是它能绕过软删除转换的原因，
        // 也意味着它默认不加入工作单元的物理事务。先幂等地把事务开起来，
        // 否则在手动触发（端点包在 UoW 里）时，外层回滚撤不掉已经删掉的行。
        var repository = _serviceProvider.GetService<IRepository<TEntity>>();
        if (repository != null)
        {
            await repository.EnsureTransactionStartedAsync(cancellationToken);
        }

        return await dbContext.Set<TEntity>()
            .IgnoreQueryFilters()
            .Where(predicate)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// 找出承载该实体的 DbContext 实例。
    /// </summary>
    private DbContext ResolveDbContext<TEntity>() where TEntity : class
    {
        var dbContextType = _entityManager.GetDbContextTypeForEntity(typeof(TEntity));

        // GetDbContextTypeForEntity 对未注册实体返回 typeof(object) 占位符。
        if (dbContextType == null || dbContextType == typeof(object))
        {
            throw new InvalidOperationException(
                $"No DbContext is registered for entity type '{typeof(TEntity).FullName}'. "
                + "A retention policy can only target entities that belong to a Tnzi DbContext.");
        }

        return _serviceProvider.GetService(dbContextType) as DbContext
            ?? throw new InvalidOperationException(
                $"DbContext '{dbContextType.Name}' could not be resolved from the service provider.");
    }

    /// <summary>
    /// 构造 <c>e =&gt; keys.Contains(EF.Property&lt;TKey&gt;(e, "Id"))</c>。
    /// </summary>
    /// <remarks>
    /// 用 <c>EF.Property</c> 而不是直接访问属性，是为了不给 <typeparamref name="TEntity"/>
    /// 强加「主键必须叫 Id」的约束——主键名与类型都从 EF 模型里读。
    /// </remarks>
    private static Expression<Func<TEntity, bool>> BuildPrimaryKeyPredicate<TEntity>(
        IEntityType efEntityType,
        IReadOnlyList<TEntity> entities)
        where TEntity : class, IEntity
    {
        var primaryKey = efEntityType.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).FullName}' has no primary key and cannot be destroyed by "
                + $"{nameof(HardDeleteDataDestroyer)}.");

        if (primaryKey.Properties.Count != 1)
        {
            throw new NotSupportedException(
                $"Entity type '{typeof(TEntity).FullName}' has a composite primary key. "
                + $"{nameof(HardDeleteDataDestroyer)} only supports single-property keys; "
                + $"register a custom {nameof(IDataDestroyer)} for this entity.");
        }

        var keyProperty = primaryKey.Properties[0];
        var keyClrType = keyProperty.ClrType;

        // 键值要装进强类型集合，Contains 才能翻译成 SQL 的 IN。
        var listType = typeof(List<>).MakeGenericType(keyClrType);
        var keys = (System.Collections.IList)Activator.CreateInstance(listType)!;
        foreach (var entity in entities)
        {
            keys.Add(entity.GetKeys()[0]);
        }

        var parameter = Expression.Parameter(typeof(TEntity), "e");
        var propertyAccess = Expression.Call(
            EfPropertyMethod.MakeGenericMethod(keyClrType),
            parameter,
            Expression.Constant(keyProperty.Name));

        var body = Expression.Call(
            ContainsMethod.MakeGenericMethod(keyClrType),
            Expression.Constant(keys, listType),
            propertyAccess);

        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }
}
