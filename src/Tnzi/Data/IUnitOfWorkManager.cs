namespace Tnzi.Data;

/// <summary>
/// 工作单元管理器接口
/// 用于管理多个 DbContext 的工作单元，支持统一的事务协调
/// </summary>
public interface IUnitOfWorkManager
{
    /// <summary>
    /// 保存所有 DbContext 的更改
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 开始所有 DbContext 的事务
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 提交所有 DbContext 的事务
    /// </summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 回滚所有 DbContext 的事务
    /// </summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定类型的 UnitOfWork
    /// </summary>
    IUnitOfWork? GetUnitOfWork<T>();

    /// <summary>
    /// 获取指定类型的 UnitOfWork
    /// </summary>
    IUnitOfWork? GetUnitOfWork(Type type);
    
    /// <summary>
    /// 启用事务（为所有 UnitOfWork 启用事务，支持嵌套）
    /// </summary>
    void EnableTransaction();
    
    /// <summary>
    /// 获取是否已启用事务
    /// </summary>
    bool IsEnabledTransaction { get; }
    
    /// <summary>
    /// 获取事务嵌套深度
    /// </summary>
    int TransactionDepth { get; }
}

