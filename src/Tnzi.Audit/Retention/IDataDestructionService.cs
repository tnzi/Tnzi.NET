namespace Tnzi.Audit.Retention;

/// <summary>
/// 策略驱动的数据销毁：扫描到期记录、排除诉讼保全、销毁并出具证明。
/// </summary>
/// <remarks>
/// <para>
/// 定时由 <c>DataDestructionBackgroundService</c> 调用；也可从管理端手动触发
/// （首次上线时通常先开 <c>DryRun</c> 手动跑一次，看清楚它准备删多少）。
/// </para>
/// <para>
/// <strong>未启用时所有方法都是空操作</strong>（返回成功且零条数）。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "保留策略的声明形态与销毁证明字段仍在演进")]
public interface IDataDestructionService
{
    /// <summary>
    /// 跑一轮销毁：对每条已声明的保留策略各扫描一次。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 每条策略的结果汇总。<strong>单条策略失败不会中断其余策略</strong>——
    /// 一条策略的实体类型配错了，不该让其它策略的到期数据一直堆着；
    /// 失败原因记在该策略的 <c>Error</c> 上。
    /// </returns>
    Task<Result<DataDestructionRunDto>> RunAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验销毁证明链是否完整未被篡改。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 链完整时成功；发现断链时失败，消息中包含<strong>第一个</strong>校验失败的序号。
    /// </returns>
    /// <remarks>
    /// 「销毁记录被人事后删掉了」与「本来就没销毁过」在没有链的情况下无法区分，
    /// 这正是链存在的理由。
    /// </remarks>
    Task<Result> VerifyChainAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页查询销毁证明。
    /// </summary>
    /// <param name="query">查询条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 一份没人查得到的证明等于不存在——「可证明」要求证据是可被取出示人的。
    /// </remarks>
    Task<Result<IPagedList<DataDestructionDto>>> GetCertificatesAsync(
        DataDestructionQueryDto query,
        CancellationToken cancellationToken = default);
}
