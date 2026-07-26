
namespace Tnzi.EFCore.Extensions;

/// <summary>
/// UnitOfWork 扩展方法
/// </summary>
public static class UnitOfWorkExtensions
{
    /// <summary>
    /// 从服务提供者获取 UnitOfWork，并可选择性地启用事务
    /// 借鉴旧框架的 GetUnitOfWork 扩展方法
    /// </summary>
    /// <param name="provider">服务提供者</param>
    /// <param name="enableTransaction">是否启用事务（调用 EnableTransaction）</param>
    /// <returns>工作单元实例</returns>
    public static IUnitOfWork? GetUnitOfWork(this IServiceProvider provider, bool enableTransaction = false)
    {
        Check.NotNull(provider);

        // 尝试获取单个 IUnitOfWork（如果有注册）
        var unitOfWork = provider.GetService<IUnitOfWork>();
        if (unitOfWork != null)
        {
            if (enableTransaction)
            {
                unitOfWork.EnableTransaction();
            }
            return unitOfWork;
        }

        // 未注册 IUnitOfWork（未配置 DbContext）时返回 null
        return null;
    }

    /// <summary>
    /// 在事务中执行操作（便捷方法）
    /// </summary>
    /// <param name="unitOfWorkManager">工作单元管理器</param>
    /// <param name="operation">要执行的操作</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task ExecuteInTransactionAsync(
        this IUnitOfWorkManager unitOfWorkManager,
        Func<Task> operation,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(unitOfWorkManager);
        Check.NotNull(operation);

        unitOfWorkManager.EnableTransaction();
        try
        {
            await operation();
            await unitOfWorkManager.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await unitOfWorkManager.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// 在事务中执行操作并返回结果（便捷方法）
    /// </summary>
    /// <typeparam name="T">返回类型</typeparam>
    /// <param name="unitOfWorkManager">工作单元管理器</param>
    /// <param name="operation">要执行的操作</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public static async Task<T> ExecuteInTransactionAsync<T>(
        this IUnitOfWorkManager unitOfWorkManager,
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(unitOfWorkManager);
        Check.NotNull(operation);

        unitOfWorkManager.EnableTransaction();
        try
        {
            var result = await operation();
            await unitOfWorkManager.CommitTransactionAsync(cancellationToken);
            return result;
        }
        catch
        {
            await unitOfWorkManager.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// 在事务中执行操作（便捷方法，使用单个 UnitOfWork）
    /// </summary>
    /// <param name="unitOfWork">工作单元</param>
    /// <param name="operation">要执行的操作</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task ExecuteInTransactionAsync(
        this IUnitOfWork unitOfWork,
        Func<Task> operation,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(unitOfWork);
        Check.NotNull(operation);

        unitOfWork.EnableTransaction();
        try
        {
            await operation();
            await unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// 在事务中执行操作并返回结果（便捷方法，使用单个 UnitOfWork）
    /// </summary>
    /// <typeparam name="T">返回类型</typeparam>
    /// <param name="unitOfWork">工作单元</param>
    /// <param name="operation">要执行的操作</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public static async Task<T> ExecuteInTransactionAsync<T>(
        this IUnitOfWork unitOfWork,
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(unitOfWork);
        Check.NotNull(operation);

        unitOfWork.EnableTransaction();
        try
        {
            var result = await operation();
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            return result;
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}