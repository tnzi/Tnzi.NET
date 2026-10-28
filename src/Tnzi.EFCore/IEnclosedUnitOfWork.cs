
namespace Tnzi.EFCore;

/// <summary>
/// 管理器的工作单元可能加入别人持有的物理事务：作用域里直接注入的 <see cref="IUnitOfWork"/> 先进入了自己的事务，
/// 管理器后开事务时，它为同一个上下文建的工作单元不另开 BEGIN，而是加入那个事务（见 <see cref="EFCoreUnitOfWork{TDbContext}"/>）。
/// </summary>
internal interface IEnclosedUnitOfWork
{
    /// <summary>
    /// 最近一次最外层提交没有物理提交：它只 flush 进了外层作用域实例持有的物理事务。
    /// 管理器据此不执行提交后队列，交给持有者在真正提交之后执行（回滚则作废）。
    /// </summary>
    bool LastCommitDeferredToEnclosingTransaction { get; }
}
