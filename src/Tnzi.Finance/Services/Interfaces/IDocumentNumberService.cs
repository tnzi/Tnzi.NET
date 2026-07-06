namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 单据连续编号服务（按租户 + 作用域，无缺口）
/// </summary>
/// <remarks>
/// 与 Snowflake 流水号互补：本服务提供满足法定连续性要求的编号
/// （会计凭证号、发票号等）。分配依赖数据库行锁串行化：
/// <para>
/// - 在活动事务（工作单元）内调用时，事务回滚会连同号码一起回收，保证无缺口；
///   本服务自动加入调用方的事务。
/// </para>
/// <para>
/// - 无事务调用时服务会自建事务保证分配正确性，但调用方后续失败不会回收号码
///   （产生缺口）。需要严格无缺口时，请在工作单元内调用。
/// </para>
/// </remarks>
public interface IDocumentNumberService
{
    /// <summary>
    /// 分配下一个序列号
    /// </summary>
    /// <param name="scope">序列作用域（如 "JournalEntry"、"Invoice"）</param>
    Task<long> NextAsync(string scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分配下一个序列号并格式化（prefix + 补零数字）
    /// </summary>
    Task<string> NextFormattedAsync(string scope, string? prefix = null, int padding = 0, CancellationToken cancellationToken = default);
}
