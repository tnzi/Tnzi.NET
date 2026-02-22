namespace Tnzi.AspNetCore.Mvc.Filters;

/// <summary>
/// 模型验证特性，用于控制是否进行模型验证
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ModelStateValidationAttribute : Attribute
{
    /// <summary>
    /// 获取或设置 是否忽略验证（默认false）
    /// </summary>
    public bool Ignore { get; set; } = false;
}

