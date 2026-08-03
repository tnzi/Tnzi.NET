namespace Tnzi.Audit.Services;

/// <summary>
/// 操作审计服务接口
/// </summary>
public interface IAuditOperationService
{
    /// <summary>
    /// 获取操作审计
    /// </summary>
    /// <param name="id">操作审计ID</param>
    /// <returns>操作审计 DTO</returns>
    Task<Result<AuditOperationDto>> GetAsync(Guid id);

    /// <summary>
    /// 获取用户的操作审计列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <param name="resultType">结果类型（可选）</param>
    /// <returns>操作审计 DTO 列表</returns>
    Task<Result<IEnumerable<AuditOperationDto>>> GetUserOperationsAsync(
        Guid userId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        AuditResultType? resultType = null);

    /// <summary>
    /// 获取操作审计列表（分页）
    /// </summary>
    /// <param name="query">查询条件</param>
    /// <returns>分页的操作审计 DTO 列表</returns>
    Task<Result<IPagedList<AuditOperationDto>>> GetOperationsAsync(AuditOperationQueryDto query);

    /// <summary>
    /// 获取功能的操作统计
    /// </summary>
    /// <param name="functionName">功能名称</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <returns>操作统计信息</returns>
    Task<Result<AuditOperationStatistics>> GetFunctionStatisticsAsync(
        string functionName,
        DateTime? startDate = null,
        DateTime? endDate = null);

    /// <summary>
    /// 获取用户的操作统计
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <returns>操作统计信息</returns>
    Task<Result<AuditOperationStatistics>> GetUserStatisticsAsync(
        Guid userId,
        DateTime? startDate = null,
        DateTime? endDate = null);

    /// <summary>
    /// 删除过期操作审计
    /// </summary>
    /// <param name="days">保留天数</param>
    /// <returns>删除的记录数</returns>
    Task<Result<int>> DeleteExpiredOperationsAsync(int? days = null);

    /// <summary>
    /// Export audit operations as CSV string
    /// </summary>
    /// <param name="query">Query filter criteria</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>CSV formatted string</returns>
    Task<Result<string>> ExportToCsvAsync(AuditOperationQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Export audit operations as JSON string
    /// </summary>
    /// <param name="query">Query filter criteria</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON formatted string</returns>
    Task<Result<string>> ExportToJsonAsync(AuditOperationQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取审计操作趋势统计（按时间分组）
    /// </summary>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <param name="groupBy">分组方式</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>趋势数据点列表</returns>
    Task<Result<List<AuditTrendPointDto>>> GetAuditTrendAsync(
        DateTime startDate,
        DateTime endDate,
        TrendInterval groupBy = TrendInterval.Daily,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<List<AuditTrendPointDto>>("GetAuditTrendAsync not implemented", 501));

    /// <summary>
    /// 获取 Top N 调用量最高的功能统计
    /// </summary>
    /// <param name="topN">返回数量</param>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Top 功能统计列表</returns>
    Task<Result<List<TopFunctionDto>>> GetTopFunctionsAsync(
        int topN = 10,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<List<TopFunctionDto>>("GetTopFunctionsAsync not implemented", 501));

    /// <summary>
    /// 获取 Top N 活跃用户统计
    /// </summary>
    /// <param name="topN">返回数量</param>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Top 用户统计列表</returns>
    Task<Result<List<TopUserDto>>> GetTopUsersAsync(
        int topN = 10,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<List<TopUserDto>>("GetTopUsersAsync not implemented", 501));
}
