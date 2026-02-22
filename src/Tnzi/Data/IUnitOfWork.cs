namespace Tnzi.Data;

/// <summary>
/// 工作单元接口
/// </summary>
public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// 保存更改
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 开始事务（如果已启用事务，则只增加嵌套深度）
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 提交事务（只在嵌套深度为 1 时真正提交）
    /// </summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 回滚事务（回滚所有嵌套事务）
    /// </summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 启用事务（标记需要事务，但不立即开始，支持嵌套）
    /// 这是延迟事务开始的关键：调用此方法后，事务会在第一次 SaveChanges 时才真正开始
    /// </summary>
    void EnableTransaction();
    
    /// <summary>
    /// 获取是否已启用事务（是否已调用 EnableTransaction）
    /// </summary>
    bool IsEnabledTransaction { get; }
    
    /// <summary>
    /// 获取事务嵌套深度
    /// </summary>
    int TransactionDepth { get; }

    /// <summary>
    /// 创建保存点
    /// </summary>
    Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Savepoints are not supported by this implementation.");

    /// <summary>
    /// 回滚到保存点
    /// </summary>
    Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Savepoints are not supported by this implementation.");

    /// <summary>
    /// 释放保存点
    /// </summary>
    Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Savepoints are not supported by this implementation.");
}

