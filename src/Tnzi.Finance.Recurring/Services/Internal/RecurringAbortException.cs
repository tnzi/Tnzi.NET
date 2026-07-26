namespace Tnzi.Finance.Recurring.Services.Internal;

/// <summary>
/// 中止当前期次的事务并把失败原因带出去
/// </summary>
/// <remarks>
/// 与 Finance 核心的 <c>UnitOfWorkAbortException</c> 同一铁律：
/// <c>ExecuteInUnitOfWorkAsync</c> 只在**异常**时回滚，lambda 返回失败 Result 仍会
/// 正常提交 —— 这里已经先插了一行生成记录，用 return 传递失败会把那行留在库里，
/// 而它是幂等键：这一期从此再也不会被重试。
///
/// 本模块自带一个而不是复用核心那个：核心的是 <c>internal</c>，为一个控制流异常
/// 开 friend 声明得不偿失，何况两边的语义边界本就各自独立。
/// </remarks>
internal sealed class RecurringAbortException : Exception
{
    /// <summary>要带回给调用方的失败结果</summary>
    public Result Result { get; }

    public RecurringAbortException(Result result) : base(result.Message)
    {
        Result = Check.NotNull(result);
    }
}
