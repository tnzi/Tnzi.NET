using System.Globalization;
using System.Text.RegularExpressions;

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

/// <summary>
/// 简单 Prompt 模板引擎 — 支持 {{variable}} 变量替换
/// </summary>
/// <remarks>
/// <para>
/// 内置变量：
/// - {{date}} — 当前 UTC 日期（yyyy-MM-dd）
/// - {{time}} — 当前 UTC 时间（HH:mm:ss）
/// - {{datetime}} — 当前 UTC 日期时间（yyyy-MM-dd HH:mm:ss）
/// - {{user.name}} — 当前用户名（需注入 ICurrentUser）
/// - {{user.id}} — 当前用户 ID
/// - {{agent.name}} — Agent 名称（通过自定义变量传入）
/// </para>
/// <para>
/// 未匹配的变量保留原始占位符不替换，避免运行时异常。
/// </para>
/// </remarks>
public partial class SimplePromptTemplateEngine : IPromptTemplateEngine
{
    private readonly ICurrentUser? _currentUser;

    public SimplePromptTemplateEngine(ICurrentUser? currentUser = null)
    {
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public string Render(string template, IDictionary<string, string>? variables = null)
    {
        if (string.IsNullOrEmpty(template) || !template.Contains("{{", StringComparison.Ordinal))
        {
            return template;
        }

        var builtInVars = BuildBuiltInVariables();

        return TemplateVariableRegex().Replace(template, match =>
        {
            var key = match.Groups[1].Value.Trim();

            // 自定义变量优先
            if (variables != null && variables.TryGetValue(key, out var customValue))
            {
                return customValue;
            }

            // 内置变量
            if (builtInVars.TryGetValue(key, out var builtInValue))
            {
                return builtInValue;
            }

            // 未匹配的变量保留原始占位符
            return match.Value;
        });
    }

    private Dictionary<string, string> BuildBuiltInVariables()
    {
        var now = DateTime.UtcNow;
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["date"] = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["time"] = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            ["datetime"] = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
        };

        if (_currentUser != null)
        {
            vars["user.name"] = _currentUser.UserName ?? string.Empty;
            vars["user.id"] = _currentUser.Id?.ToString() ?? string.Empty;
        }

        return vars;
    }

    [GeneratedRegex(@"\{\{(\s*[\w.]+\s*)\}\}", RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TemplateVariableRegex();
}
