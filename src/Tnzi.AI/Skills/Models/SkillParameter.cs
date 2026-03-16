namespace Tnzi.AI.Skills.Models;

/// <summary>
/// 技能参数定义
/// </summary>
public class SkillParameter
{
    /// <summary>参数名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>参数描述</summary>
    public string? Description { get; set; }

    /// <summary>默认值</summary>
    public string? DefaultValue { get; set; }

    /// <summary>是否必填</summary>
    public bool Required { get; set; }

    /// <summary>允许的枚举值</summary>
    public List<string>? AllowedValues { get; set; }
}
