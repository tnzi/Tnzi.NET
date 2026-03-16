namespace Tnzi.AI.Skills;

/// <summary>
/// 技能模板引擎 — 解析 {{param}} 占位符、验证参数、渲染最终提示词
/// </summary>
public interface ISkillTemplateEngine
{
    /// <summary>渲染技能模板</summary>
    SkillRenderResult Render(SkillDefinition skill, Dictionary<string, string>? parameters = null);

    /// <summary>提取模板中所有参数占位符名称</summary>
    IReadOnlyList<string> ExtractParameterNames(string template);
}

/// <summary>模板渲染结果</summary>
public class SkillRenderResult
{
    public bool Success { get; set; }
    public string RenderedContent { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = [];
    public List<string> UnusedParameters { get; set; } = [];
}
