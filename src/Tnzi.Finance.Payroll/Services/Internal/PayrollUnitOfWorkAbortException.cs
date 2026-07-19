namespace Tnzi.Finance.Payroll.Services.Internal;

/// <summary>
/// 工作单元中止信号：在 UoW lambda 内部、已发生写入之后遇到业务失败时抛出，
/// 强制 ExecuteInUnitOfWorkAsync 回滚整个事务
/// </summary>
/// <remarks>
/// 语义同 Finance 的同名内部异常（该异常 internal 引不到，本子模块自持一份）：
/// ExecuteInUnitOfWorkAsync 只在**异常**时回滚——lambda 返回失败 Result 仍会正常提交，
/// 变更跟踪会把此前迭代已发生的写入一并落库（部分提交）。因此多凭证过账/付款/作废循环内
/// 的中途失败 MUST 以本异常传递（而非 return Result.Failure），调用方在
/// ExecuteInUnitOfWorkAsync 外层捕获并转换回 Result。
/// </remarks>
internal sealed class PayrollUnitOfWorkAbortException : Exception
{
    /// <summary>要返回给调用方的业务失败结果</summary>
    public Result Result { get; }

    public PayrollUnitOfWorkAbortException(Result result) : base(result.Message)
    {
        Result = Check.NotNull(result);
    }
}
