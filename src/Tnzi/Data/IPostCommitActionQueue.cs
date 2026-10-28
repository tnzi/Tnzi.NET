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
    /// <param name="action">
    /// 要延迟执行的异步操作。它收到的令牌由执行队列的一方决定，<b>不是</b>请求的取消令牌：
    /// 工作单元管理器在事务提交之后执行队列，传的是不可取消的令牌：事务已经提交，
    /// 客户端此时断开不应让提交后的副作用被取消而丢失。
    /// </param>
    void Enqueue(Func<CancellationToken, Task> action);

    /// <summary>
    /// 执行队列中的所有操作并清空队列
    /// </summary>
    /// <remarks>
    /// 队列里的动作彼此独立，实现 MUST 在其中一个失败时继续执行其余动作，
    /// 并在返回前清空队列（滞留的动作会被下一个事务的回滚清掉，或在无关时刻被执行）。
    /// 失败合并为一个 <see cref="AggregateException"/> 抛出。事务此时已经提交，
    /// 调用方通常只应记录，不应据此让事务失败。
    /// </remarks>
    /// <exception cref="AggregateException">一个或多个操作失败。</exception>
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
