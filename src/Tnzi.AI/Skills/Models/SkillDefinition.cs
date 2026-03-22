namespace Tnzi.AI.Skills.Models;

/// <summary>
/// 技能定义 - 表示一个可用的 AI 技能
/// </summary>
/// <remarks>
/// <para>
/// 技能是结构化的 AI 能力描述，通常以 SKILL.md 文件形式存在。
/// 包含技能的名称、描述、使用场景、依赖要求等信息。
/// </para>
/// <para>
/// 技能与工具的区别：
/// <list type="bullet">
/// <item><description>工具（Tool）：可执行的函数，直接由 AI 调用</description></item>
/// <item><description>技能（Skill）：能力描述，指导 AI 如何处理特定类型的任务</description></item>
/// </list>
/// </para>
/// </remarks>
public class SkillDefinition
{
    /// <summary>
    /// 技能唯一标识（人类可读的 slug，如 "code-review"）
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// 技能作用域
    /// </summary>
    public SkillScope Scope { get; set; }

    /// <summary>
    /// 技能名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 技能描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 详细内容（SKILL.md 的完整内容）
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 使用场景说明
    /// </summary>
    public string? WhenToUse { get; set; }

    /// <summary>
    /// 技能参数定义
    /// </summary>
    public List<SkillParameter> Parameters { get; set; } = [];

    /// <summary>
    /// 技能版本
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// 技能作者
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// 技能标签
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// 依赖要求
    /// </summary>
    public SkillRequirements? Requirements { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 优先级（数值越大优先级越高）
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Skill 激活时限定可用工具组（对齐 Claude Skills 的 contextModifier）
    /// </summary>
    /// <remarks>
    /// 非空时，Agent 运行时应仅暴露列出的工具组给模型。
    /// 不在框架层强制执行，由调用方决定如何使用。
    /// </remarks>
    public List<string>? AllowedToolGroups { get; set; }

    /// <summary>
    /// 单工具白名单（组级 AllowedToolGroups 之外额外放行的单个工具）
    /// </summary>
    public List<string>? AllowedTools { get; set; }

    /// <summary>
    /// 单工具黑名单（优先级最高，即使在 AllowedToolGroups 组中也会被移除）
    /// </summary>
    public List<string>? DeniedTools { get; set; }

    /// <summary>
    /// Skill 激活时要求使用的模型
    /// </summary>
    public string? RequiredModel { get; set; }

    /// <summary>
    /// Skill 激活时要求使用的 provider
    /// </summary>
    public string? RequiredProvider { get; set; }

    /// <summary>
    /// 技能文件路径（来源为 FileSystem 时有值）
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// 技能来源
    /// </summary>
    public SkillSource Source { get; set; }
}

/// <summary>
/// 技能依赖要求
/// </summary>
public class SkillRequirements
{
    /// <summary>
    /// 必需的可执行文件
    /// </summary>
    public List<string> Bins { get; set; } = [];

    /// <summary>
    /// 必需的环境变量
    /// </summary>
    public List<string> Envs { get; set; } = [];

    /// <summary>
    /// 必需的配置项
    /// </summary>
    public List<string> Configs { get; set; } = [];

    /// <summary>
    /// 支持的操作系统
    /// </summary>
    public List<string> Os { get; set; } = [];

    /// <summary>
    /// 必需的工具组
    /// </summary>
    public List<string> ToolGroups { get; set; } = [];
}

/// <summary>技能作用域</summary>
public enum SkillScope { System = 0, Tenant = 1, User = 2 }

/// <summary>技能来源</summary>
public enum SkillSource { FileSystem, Database }
