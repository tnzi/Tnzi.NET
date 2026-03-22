namespace Tnzi.AI;

/// <summary>
/// AI 中间件执行顺序常量 — 数值越小越先执行
/// </summary>
public static class AiMiddlewareOrders
{
    /// <summary>扩展思考注入（最先）</summary>
    public const int Thinking = 50;

    /// <summary>Prompt 缓存标记注入</summary>
    public const int PromptCaching = 75;

    /// <summary>配额预检与预留</summary>
    public const int Quota = 100;

    /// <summary>输入安全防护</summary>
    public const int InputGuardrail = 200;

    /// <summary>对话历史加载</summary>
    public const int History = 300;

    /// <summary>上下文注入（Memory/RAG/Skill）</summary>
    public const int ContextInjection = 400;

    /// <summary>技能约束执行（工具组/模型/Provider 过滤）</summary>
    public const int SkillConstraint = 450;

    /// <summary>用量日志记录</summary>
    public const int UsageLogging = 500;

    /// <summary>输出安全防护（最后）</summary>
    public const int OutputGuardrail = 900;
}
