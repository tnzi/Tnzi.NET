namespace Tnzi.Data;

/// <summary>
/// 环境工作单元作用域接口
/// 由工作单元管理器(如 EFCore 的 UnitOfWorkManager)实现,向基础设施组件(如事件总线)
/// 暴露"当前异步流是否处于活跃事务中"的信息,并允许把副作用延迟到事务提交后执行
/// </summary>
[ExperimentalApi(Reason = "Ambient transaction awareness API, introduced for transaction-safe event publishing; shape may evolve before 1.0")]
public interface IAmbientUnitOfWorkScope
{
    /// <summary>
    /// 当前是否处于活跃事务中
    /// </summary>
    bool IsTransactionActive { get; }

    /// <summary>
    /// 将操作排入事务提交后执行队列
    /// 事务提交成功后按入队顺序执行;事务回滚时全部丢弃
    /// </summary>
    /// <param name="action">提交后执行的操作</param>
    void EnqueuePostCommit(Func<CancellationToken, Task> action);
}

/// <summary>
/// 环境工作单元上下文(基于 AsyncLocal,随 ExecutionContext 流动)
/// 工作单元管理器在事务启用时设置当前作用域,提交/回滚后清除;
/// 事件总线等基础设施据此实现"事务中发布自动延迟到提交后"的默认安全语义
/// </summary>
[ExperimentalApi(Reason = "Ambient transaction awareness API, introduced for transaction-safe event publishing; shape may evolve before 1.0")]
public static class AmbientUnitOfWork
{
    private static readonly AsyncLocal<IAmbientUnitOfWorkScope?> _current = new();

    /// <summary>
    /// 当前异步流的环境工作单元作用域(无活跃事务时为 null)
    /// </summary>
    public static IAmbientUnitOfWorkScope? Current => _current.Value;

    /// <summary>
    /// 设置当前异步流的环境工作单元作用域(传 null 清除)
    /// 仅供工作单元管理器在事务生命周期节点调用,业务代码不应直接使用
    /// </summary>
    public static void Set(IAmbientUnitOfWorkScope? scope) => _current.Value = scope;
}
