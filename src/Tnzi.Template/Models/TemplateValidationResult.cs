namespace Tnzi.Template.Models;

/// <summary>
/// 模板验证结果
/// </summary>
public class TemplateValidationResult
{
    /// <summary>
    /// 验证是否通过
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// 错误列表
    /// </summary>
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static TemplateValidationResult Success() => new() { IsValid = true };

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static TemplateValidationResult Failed(params string[] errors) => new()
    {
        IsValid = false,
        Errors = errors.Length > 0 ? Array.AsReadOnly(errors) : Array.Empty<string>()
    };
}
