namespace Tnzi.AspNetCore.Versioning;

/// <summary>
/// API 版本特性（不生效，见备注）
/// </summary>
/// <remarks>
/// 框架里没有任何代码读取这个特性：版本由 <see cref="ApiVersionMiddleware"/> 按 <see cref="ApiVersionOptions"/>
/// 全局解析，写进 <c>HttpContext.Items["ApiVersion"]</c>，路由不按版本分派。给两个控制器标上不同版本，
/// 请求照样只按路由模板落到其中一个。需要按版本分派时请用不同的路由模板（如 <c>v2/users</c>），
/// 或在 action 里读 <c>HttpContext.Items["ApiVersion"]</c>。
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
[Obsolete("ApiVersionAttribute is not enforced: versions are resolved globally by ApiVersionMiddleware and stored in HttpContext.Items[\"ApiVersion\"]; routing does not dispatch by version.")]
public class ApiVersionAttribute : Attribute
{
    /// <summary>
    /// API 版本号
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// 是否不检查版本（仅标记版本信息）
    /// </summary>
    public bool NoCheck { get; set; }

    public ApiVersionAttribute(string version)
    {
        Version = version;
    }

    /// <summary>
    /// 当前版本
    /// </summary>
    public static ApiVersionAttribute Current => new(string.Empty) { NoCheck = true };
}
