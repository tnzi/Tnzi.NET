namespace Tnzi.AI.Coder.Security;

/// <summary>
/// 路径验证接口 — 验证文件/目录路径是否允许访问
/// </summary>
public interface IPathValidator
{
    /// <summary>
    /// 验证路径是否安全可访问
    /// </summary>
    /// <param name="path">待验证路径</param>
    /// <returns>验证结果</returns>
    Task<PathValidationResult> ValidateAsync(string path);
}

/// <summary>
/// 路径验证结果
/// </summary>
public class PathValidationResult
{
    /// <summary>
    /// 是否有效
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// 解析后的绝对路径
    /// </summary>
    public string? ResolvedPath { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static PathValidationResult Success(string resolvedPath) => new() { IsValid = true, ResolvedPath = resolvedPath };

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static PathValidationResult Failure(string error) => new() { IsValid = false, Error = error };
}
