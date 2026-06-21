namespace Tnzi.AI.Templates;

/// <summary>
/// Prompt 模板引擎接口 — 对 Agent Instructions 进行变量替换
/// </summary>
public interface IPromptTemplateEngine
{
    /// <summary>
    /// 渲染模板，将 {{variable}} 占位符替换为实际值
    /// </summary>
    /// <param name="template">模板文本（可包含 {{variable}} 占位符）</param>
    /// <param name="variables">自定义变量（优先级高于内置变量）</param>
    /// <returns>渲染后的文本</returns>
    string Render(string template, IDictionary<string, string>? variables = null);
}
