namespace Tnzi.Data;

/// <summary>
/// 事务提交后操作队列接口
/// 用于收集在事务活跃期间需要延迟到提交后执行的操作（如事件发布）
/// Scoped 生命周期，与请求/作用域绑定
/// </summary>
public interface IPostCommitActionQueue
{
    /// <summary>
    /// 将操作加入队列
    /// </summary>
    /// <param name="action">要延迟执行的异步操作</param>
    void Enqueue(Func<CancellationToken, Task> action);

    /// <summary>
    /// 执行队列中的所有操作并清空队列
    /// </summary>
    Task ExecuteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清空队列（用于事务回滚时丢弃所有待执行操作）
    /// </summary>
    void Clear();

    /// <summary>
    /// 获取队列中待执行的操作数量
    /// </summary>
    int Count { get; }
}
