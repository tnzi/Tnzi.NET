
namespace Tnzi.Audit.Dtos;

/// <summary>
/// 操作审计查询DTO
/// </summary>
public class AuditOperationQueryDto : PagedQueryDto
{
    /// <summary>
    /// 获取或设置 功能名称（可选）
    /// </summary>
    [MaxLength(200)]
    public string? FunctionName { get; set; }

    /// <summary>
    /// 获取或设置 权限名称（可选）
    /// </summary>
    [MaxLength(200)]
    public string? PermissionName { get; set; }

    /// <summary>
    /// 获取或设置 用户ID（可选）
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 获取或设置 结果类型（可选）
    /// </summary>
    public AuditResultType? ResultType { get; set; }

    /// <summary>
    /// 获取或设置 开始日期（可选）
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// 获取或设置 结束日期（可选）
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// 获取或设置 IP地址（可选）
    /// </summary>
    [MaxLength(50)]
    public string? Ip { get; set; }
}

/// <summary>
/// 操作审计统计信息
/// </summary>
public class AuditOperationStatistics
{
    /// <summary>
    /// 获取或设置 总操作数
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 获取或设置 成功操作数
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// 获取或设置 失败操作数
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// 获取或设置 警告操作数
    /// </summary>
    public int WarningCount { get; set; }

    /// <summary>
    /// 获取或设置 平均执行时间（毫秒）
    /// </summary>
    public double AverageElapsed { get; set; }

    /// <summary>
    /// 获取或设置 最大执行时间（毫秒）
    /// </summary>
    public long MaxElapsed { get; set; }

    /// <summary>
    /// 获取或设置 最小执行时间（毫秒）
    /// </summary>
    public long MinElapsed { get; set; }
}

/// <summary>
/// 审计趋势分组类型
/// </summary>
public enum AuditTrendGroupBy
{
    /// <summary>按天分组</summary>
    Daily,
    /// <summary>按周分组</summary>
    Weekly,
    /// <summary>按月分组</summary>
    Monthly
}

/// <summary>
/// 审计趋势数据点
/// </summary>
public class AuditTrendPointDto
{
    /// <summary>时间段标识（如 "2026-02-28", "2026-W09", "2026-02"）</summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>总操作数</summary>
    public int TotalCount { get; set; }

    /// <summary>成功操作数</summary>
    public int SuccessCount { get; set; }

    /// <summary>失败操作数</summary>
    public int FailedCount { get; set; }

    /// <summary>警告操作数</summary>
    public int WarningCount { get; set; }

    /// <summary>平均耗时（毫秒）</summary>
    public double AverageElapsed { get; set; }
}

/// <summary>
/// Top 功能统计 DTO
/// </summary>
public class TopFunctionDto
{
    /// <summary>功能名称</summary>
    public string FunctionName { get; set; } = string.Empty;

    /// <summary>调用次数</summary>
    public int HitCount { get; set; }

    /// <summary>平均耗时（毫秒）</summary>
    public double AverageElapsed { get; set; }

    /// <summary>最大耗时（毫秒）</summary>
    public long MaxElapsed { get; set; }

    /// <summary>错误次数</summary>
    public int ErrorCount { get; set; }

    /// <summary>错误率（0-1）</summary>
    public double ErrorRate { get; set; }
}

/// <summary>
/// Top 用户统计 DTO
/// </summary>
public class TopUserDto
{
    /// <summary>用户ID</summary>
    public Guid UserId { get; set; }

    /// <summary>用户名</summary>
    public string? UserName { get; set; }

    /// <summary>操作次数</summary>
    public int OperationCount { get; set; }

    /// <summary>成功次数</summary>
    public int SuccessCount { get; set; }

    /// <summary>失败次数</summary>
    public int FailedCount { get; set; }

    /// <summary>成功率（0-1）</summary>
    public double SuccessRate { get; set; }
}

/// <summary>
/// 审计操作 DTO
/// </summary>
public class AuditOperationDto
{
    public Guid Id { get; set; }
    public string? FunctionName { get; set; }
    public string? PermissionName { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? NickName { get; set; }
    public string? Ip { get; set; }
    public string? OperatingSystem { get; set; }
    public string? Browser { get; set; }
    public string? HttpMethod { get; set; }
    public string? Url { get; set; }
    public int? HttpStatusCode { get; set; }
    public long Elapsed { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public AuditResultType ResultType { get; set; }
    public string? Message { get; set; }
    public string? Exception { get; set; }
    public string? RequestParameters { get; set; }
    public string? ResponseResult { get; set; }
    public DateTime CreationTime { get; set; }
    public List<AuditEntityEntryDto> EntityEntries { get; set; } = new();
}

/// <summary>
/// 审计实体变更 DTO
/// </summary>
public class AuditEntityEntryDto
{
    public Guid Id { get; set; }
    public string? EntityTypeName { get; set; }
    public string? EntityTypeFullName { get; set; }
    public string? EntityId { get; set; }
    public Entities.EntityState OperationType { get; set; }
    public DateTime CreationTime { get; set; }
    public List<AuditPropertyEntryDto> PropertyEntries { get; set; } = new();
}

/// <summary>
/// 审计属性变更 DTO
/// </summary>
public class AuditPropertyEntryDto
{
    public Guid Id { get; set; }
    public string? PropertyName { get; set; }
    public string? PropertyDisplayName { get; set; }
    public string? PropertyTypeName { get; set; }
    public string? OriginalValue { get; set; }
    public string? NewValue { get; set; }
}
