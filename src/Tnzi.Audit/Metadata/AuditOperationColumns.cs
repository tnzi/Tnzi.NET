namespace Tnzi.Audit.Metadata;

/// <summary>
/// <see cref="AuditOperation"/> 各字符串列的长度上限。
/// </summary>
/// <remarks>
/// <para>
/// 实体配置（建列）与 <see cref="AuditMiddleware"/>（采集时截断）共用这一份数字，
/// 两边各写一份的结果是「列宽改了、截断没跟着改」，而那不报错。
/// </para>
/// <para>
/// ★ 截断不是格式问题而是<b>可用性攻击面</b>：审计操作按批（默认 100 条）用一条
/// <c>InsertMany</c> 落库，任何一行超列宽，SQL Server / PostgreSQL 会拒绝整条 INSERT，
/// 后台服务记一行日志后整批丢弃。于是一个匿名客户端只要发一个 600 字节的
/// <c>User-Agent</c>，就能连带抹掉同一时间窗里其他所有人的审计记录，而且可以一直重复。
/// SQLite 不检查长度上限，所以测试全绿证明不了任何事。
/// </para>
/// </remarks>
public static class AuditOperationColumns
{
    /// <summary><see cref="AuditOperation.FunctionName"/></summary>
    public const int FunctionNameMaxLength = 200;

    /// <summary><see cref="AuditOperation.PermissionName"/></summary>
    public const int PermissionNameMaxLength = 200;

    /// <summary><see cref="AuditOperation.UserName"/></summary>
    public const int UserNameMaxLength = 200;

    /// <summary><see cref="AuditOperation.NickName"/></summary>
    public const int NickNameMaxLength = 200;

    /// <summary><see cref="AuditOperation.Ip"/></summary>
    public const int IpMaxLength = 50;

    /// <summary><see cref="AuditOperation.OperatingSystem"/></summary>
    public const int OperatingSystemMaxLength = 200;

    /// <summary><see cref="AuditOperation.Browser"/></summary>
    public const int BrowserMaxLength = 200;

    /// <summary><see cref="AuditOperation.UserAgent"/></summary>
    public const int UserAgentMaxLength = 500;

    /// <summary><see cref="AuditOperation.Message"/></summary>
    public const int MessageMaxLength = 2000;

    /// <summary><see cref="AuditOperation.HttpMethod"/></summary>
    public const int HttpMethodMaxLength = 10;

    /// <summary><see cref="AuditOperation.Url"/></summary>
    public const int UrlMaxLength = 2000;

    /// <summary><see cref="AuditOperation.RequestBody"/></summary>
    public const int RequestBodyMaxLength = 8192;

    /// <summary>
    /// 把一个值裁到列宽以内；<c>null</c> 与够短的值原样返回。
    /// </summary>
    /// <remarks>
    /// 刻意不加省略号后缀：后缀会让结果再次超出列宽，而这里唯一的目标就是「装得进去」。
    /// </remarks>
    public static string? Fit(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];
}
