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
    /// 确保物理数据库事务已开启（幂等）
    /// </summary>
    /// <remarks>
    /// 框架的物理事务是延迟开启的（首次 SaveChanges 才 BEGIN）。
    /// 在事务内执行绕过变更跟踪的裸 SQL 操作（ExecuteUpdate/ExecuteDelete/Raw SQL）前
    /// MUST 调用本方法，否则这些操作会在自动提交模式下执行，脱离事务保护
    /// （行锁不持有到事务结束、回滚无法撤销）。未启用事务时为无操作。
    /// </remarks>
    Task EnsureTransactionStartedAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

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

