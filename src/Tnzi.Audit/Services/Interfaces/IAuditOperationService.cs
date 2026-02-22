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
    Task<Result<int>> DeleteExpiredOperationsAsync(int days = 90);
}
