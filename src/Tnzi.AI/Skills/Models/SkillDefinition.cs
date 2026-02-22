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
    /// 技能唯一标识
    /// </summary>
    public string Id { get; set; } = string.Empty;

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
    /// 技能文件路径
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// 使用场景说明
    /// </summary>
    public string? WhenToUse { get; set; }

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
