using System.Text.RegularExpressions;

namespace Tnzi.AI;

/// <summary>
/// AI 文本工具 — 中文检测、代码围栏解析等
/// </summary>
public static partial class AiTextHelper
{
    /// <summary>检测文本是否包含中文字符</summary>
    public static bool ContainsChinese(string text)
        => ChineseCharRegex().IsMatch(text);

    /// <summary>去除 Markdown 代码围栏包裹</summary>
    public static string StripCodeFence(string text)
        => CodeFenceStripRegex().Replace(text, "$1").Trim();

    [GeneratedRegex(@"[\u4e00-\u9fff]")]
    private static partial Regex ChineseCharRegex();

    [GeneratedRegex(@"```(?:json)?\s*([\s\S]*?)\s*```")]
    private static partial Regex CodeFenceStripRegex();
}

/// <summary>
/// 工具→中间件属性包键名常量
/// </summary>
public static class ContextPropertyKeys
{
    public const string ClarificationRequest = "ClarificationRequest";
    public const string Todos = "Todos";
    public const string ActiveSkills = "ActiveSkills";
    public const string IsSubAgent = "IsSubAgent";
    public const string SubAgentName = "SubAgentName";
    public const string CurrentRunId = "CurrentRunId";
    public const string WorkflowExecutionId = "WorkflowExecutionId";
    public const string WorkflowNodeName = "WorkflowNodeName";
    public const string ParentSessionRules = "ParentSessionRules";
}

/// <summary>
/// 中间件流式事件类型常量
/// </summary>
public static class MiddlewareEventTypes
{
    public const string Clarification = "clarification";
    public const string TodosUpdated = "todos_updated";
}
