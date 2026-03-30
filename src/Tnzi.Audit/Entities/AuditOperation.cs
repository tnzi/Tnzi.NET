
namespace Tnzi.Audit.Entities;

/// <summary>
/// 操作结果类型
/// </summary>
public enum AuditResultType
{
    /// <summary>
    /// 成功
    /// </summary>
    Success = 1,

    /// <summary>
    /// 失败
    /// </summary>
    Failed = 2,

    /// <summary>
    /// 警告
    /// </summary>
    Warning = 3
}

/// <summary>
/// 操作审计实体（不可变审计记录，仅记录创建时间）
/// </summary>
public class AuditOperation : CreationAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 执行的功能名（如：Controller.Action）
    /// </summary>
    public string FunctionName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 权限名称（用于权限关联）
    /// </summary>
    public string? PermissionName { get; set; }

    /// <summary>
    /// 获取或设置 当前用户ID
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 获取或设置 当前用户名
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// 获取或设置 当前用户昵称
    /// </summary>
    public string? NickName { get; set; }

    /// <summary>
    /// 获取或设置 当前访问IP
    /// </summary>
    public string? Ip { get; set; }

    /// <summary>
    /// 获取或设置 操作系统
    /// </summary>
    public string? OperatingSystem { get; set; }

    /// <summary>
    /// 获取或设置 浏览器
    /// </summary>
    public string? Browser { get; set; }

    /// <summary>
    /// 获取或设置 当前访问UserAgent
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// 获取或设置 操作结果类型
    /// </summary>
    public AuditResultType ResultType { get; set; } = AuditResultType.Success;

    /// <summary>
    /// 获取或设置 消息
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// 获取或设置 执行耗时（毫秒）
    /// </summary>
    public long Elapsed { get; set; }

    /// <summary>
    /// 获取或设置 HTTP方法
    /// </summary>
    public string? HttpMethod { get; set; }

    /// <summary>
    /// 获取或设置 请求URL
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// 获取或设置 HTTP状态码
    /// </summary>
    public int? HttpStatusCode { get; set; }

    /// <summary>
    /// 获取或设置 异常信息
    /// </summary>
    public string? Exception { get; set; }

    /// <summary>
    /// 获取或设置 请求参数（JSON格式）
    /// </summary>
    public string? RequestParameters { get; set; }

    /// <summary>
    /// 获取或设置 请求体（JSON格式，敏感字段已脱敏）
    /// </summary>
    public string? RequestBody { get; set; }

    /// <summary>
    /// 获取或设置 响应结果（JSON格式，可选）
    /// </summary>
    public string? ResponseResult { get; set; }

    /// <summary>
    /// 获取或设置 开始时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 获取或设置 结束时间
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 获取或设置 租户ID
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 获取或设置 关联的审计实体条目
    /// </summary>
    public virtual ICollection<AuditEntityEntry> EntityEntries { get; set; } = new List<AuditEntityEntry>();
}
