namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 工作单元中止信号：在 UoW lambda 内部、已发生写入之后遇到业务失败时抛出，
/// 强制 ExecuteInUnitOfWorkAsync 回滚整个事务
/// </summary>
/// <remarks>
/// ExecuteInUnitOfWorkAsync 只在**异常**时回滚——lambda 返回失败 Result 仍会正常提交，
/// 变更跟踪会把此前迭代已发生的写入一并落库（部分提交）。因此多写入循环内的
/// 中途失败 MUST 以本异常传递（而非 return Result.Failure），调用方在
/// ExecuteInUnitOfWorkAsync 外层捕获并转换回 Result。
/// 单写入或"失败必在触碰实体前返回"的缓冲式流程无需本异常。
/// </remarks>
internal sealed class UnitOfWorkAbortException : Exception
{
    /// <summary>要返回给调用方的业务失败结果</summary>
    public Result Result { get; }

    public UnitOfWorkAbortException(Result result) : base(result.Message)
    {
        Result = Check.NotNull(result);
    }
}
