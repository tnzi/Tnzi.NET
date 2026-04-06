namespace Tnzi.AI.Skills;

/// <summary>
/// 技能模板引擎 — 解析 {{param}} 占位符、验证参数、渲染最终提示词
/// </summary>
public interface ISkillTemplateEngine
{
    /// <summary>渲染技能模板</summary>
    SkillRenderResult Render(SkillDefinition skill, Dictionary<string, string>? parameters = null);

    /// <summary>
    /// 渲染技能模板（支持上下文内置变量）。
    /// Built-in variables like {{SESSION_ID}} and {{AGENT_NAME}} are automatically resolved from context.
    /// </summary>
    SkillRenderResult Render(SkillDefinition skill, Dictionary<string, string>? parameters, SkillRenderContext? context)
    {
        // Default implementation delegates to the simpler overload
        return Render(skill, parameters);
    }

    /// <summary>提取模板中所有参数占位符名称</summary>
    IReadOnlyList<string> ExtractParameterNames(string template);
}

/// <summary>
/// 模板渲染上下文 — 提供内置变量（SESSION_ID, AGENT_NAME 等）
/// </summary>
public class SkillRenderContext
{
    /// <summary>当前会话/线程 ID</summary>
    public string? SessionId { get; set; }

    /// <summary>当前 Agent 名称</summary>
    public string? AgentName { get; set; }
}

/// <summary>模板渲染结果</summary>
public class SkillRenderResult
{
    public bool Success { get; set; }
    public string RenderedContent { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = [];
    public List<string> UnusedParameters { get; set; } = [];
}
