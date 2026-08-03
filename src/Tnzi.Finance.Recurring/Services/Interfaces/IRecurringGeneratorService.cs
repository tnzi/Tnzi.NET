namespace Tnzi.Finance.Recurring.Services;

/// <summary>
/// 到期生成
/// </summary>
public interface IRecurringGeneratorService
{
    /// <summary>
    /// 扫描全部到期模板并生成。
    /// </summary>
    /// <remarks>
    /// 后台作业与"立即运行"按钮走的是同一个方法：两条路径分别实现，迟早会在补齐
    /// 语义上分叉，而那种分叉只会在月底被发现。
    /// </remarks>
    /// <param name="asOf">扫描基准日（null = 今天）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<Result<RecurringSweepResultDto>> RunDueAsync(DateTime? asOf = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 只跑一条模板（手工触发）。
    /// </summary>
    /// <remarks>
    /// 幂等键仍然生效：同一期次已经生成过就不会再来一张，重复点击是安全的。
    /// </remarks>
    Task<Result<RecurringSweepResultDto>> RunOneAsync(Guid recurringDocumentId, DateTime? asOf = null, CancellationToken cancellationToken = default);
}
