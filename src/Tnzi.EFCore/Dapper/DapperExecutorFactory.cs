
namespace Tnzi.EFCore.Dapper;

/// <summary>
/// Dapper 执行器工厂
/// 用于创建 IDapperExecutor 实例
/// </summary>
public class DapperExecutorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEntityManager _entityManager;
    private readonly IConfiguration? _configuration;

    public DapperExecutorFactory(
        IServiceProvider serviceProvider,
        IEntityManager entityManager,
        IConfiguration? configuration = null)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _entityManager = Check.NotNull(entityManager);
        _configuration = configuration;
    }

    /// <summary>
    /// 创建 Dapper 执行器实例
    /// </summary>
    public IDapperExecutor<TEntity, TKey> Create<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
    {
        return new DapperExecutor<TEntity, TKey>(_serviceProvider, _entityManager, _configuration);
    }
}