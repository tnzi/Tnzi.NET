
namespace Tnzi.AspNetCore.Versioning;

/// <summary>
/// API 版本特性
/// 用于标记控制器或方法的版本
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
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