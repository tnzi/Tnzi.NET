namespace Tnzi.ScopedContext;

/// <summary>
/// 作用域上下文接口
/// 用于在请求/作用域范围内传递上下文数据
/// </summary>
public interface IScopedContext
{
    /// <summary>
    /// 当前用户标识（用户ID）
    /// </summary>
    string? CurrentUserId { get; set; }

    /// <summary>
    /// 当前用户名
    /// </summary>
    string? CurrentUserName { get; set; }

    /// <summary>
    /// 当前用户角色列表
    /// </summary>
    IReadOnlyList<string> CurrentUserRoles { get; set; }

    /// <summary>
    /// 审计操作信息
    /// </summary>
    AuditOperationInfo? AuditOperation { get; set; }

    /// <summary>
    /// 客户端IP地址
    /// </summary>
    string? ClientIpAddress { get; set; }

    /// <summary>
    /// 用户代理字符串
    /// </summary>
    string? UserAgent { get; set; }

    /// <summary>
    /// 请求跟踪ID（用于日志关联）
    /// </summary>
    string? TraceId { get; set; }

    /// <summary>
    /// 自定义数据字典（用于扩展）
    /// </summary>
    IDictionary<string, object> Items { get; }

    /// <summary>
    /// 设置自定义项
    /// </summary>
    void SetItem<T>(string key, T value);

    /// <summary>
    /// 获取自定义项
    /// </summary>
    T? GetItem<T>(string key);
}

