namespace Tnzi.AI.Skills;

/// <summary>
/// 技能约束执行器 - 对工具组、模型、Provider 进行约束过滤
/// </summary>
public interface ISkillConstraintEnforcer
{
    /// <summary>应用技能约束</summary>
    SkillConstraintResult Apply(SkillDefinition skill, SkillConstraintContext context);
}

/// <summary>约束执行上下文</summary>
public class SkillConstraintContext
{
    /// <summary>当前可用工具组</summary>
    public List<string> AvailableToolGroups { get; set; } = [];

    /// <summary>当前模型</summary>
    public string? CurrentModel { get; set; }

    /// <summary>当前 Provider</summary>
    public string? CurrentProvider { get; set; }
}

/// <summary>约束执行结果</summary>
public class SkillConstraintResult
{
    /// <summary>约束后可用工具组</summary>
    public List<string> EffectiveToolGroups { get; set; } = [];

    /// <summary>单工具白名单</summary>
    public List<string>? EffectiveTools { get; set; }

    /// <summary>单工具黑名单</summary>
    public List<string>? DeniedTools { get; set; }

    /// <summary>约束后模型</summary>
    public string? EffectiveModel { get; set; }

    /// <summary>约束后 Provider</summary>
    public string? EffectiveProvider { get; set; }

    /// <summary>被过滤掉的工具组（供日志/审计）</summary>
    public List<string> FilteredOutToolGroups { get; set; } = [];
}
