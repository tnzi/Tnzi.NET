namespace Tnzi.AI.Tools;

/// <summary>
/// 澄清请求工具 — AI Agent 用于向用户提问以获取缺失信息
/// </summary>
[AIToolGroup("clarification")]
public class ClarificationTools
{
    private readonly IAgentExecutionContextAccessor _contextAccessor;

    public ClarificationTools(IAgentExecutionContextAccessor contextAccessor)
    {
        _contextAccessor = Check.NotNull(contextAccessor);
    }

    /// <summary>
    /// Ask the user a clarification question. Use this tool when you need more information
    /// from the user before proceeding. MANDATORY scenarios:
    /// - Missing critical information that affects the outcome
    /// - Ambiguous requirements that could lead to wrong results
    /// - Multiple valid approaches where user preference matters
    /// - Operations with significant risk that need explicit confirmation
    /// </summary>
    [AIFunction("ask_clarification",
        Description = "Ask the user a clarification question when you need more information to proceed correctly. This will pause execution and wait for the user's response.",
        InterruptBehavior = ToolInterruptBehavior.GracefulShutdown)]
    public string AskClarification(
        [Description("The clarification question to ask the user")] string question,
        [Description("Type of clarification: MissingInfo, AmbiguousRequirement, ApproachChoice, RiskConfirmation, Suggestion")] ClarificationType type,
        [Description("Additional context explaining why clarification is needed")] string? context = null,
        [Description("Numbered options for the user to choose from (for ApproachChoice type)")] List<string>? options = null)
    {
        // 将澄清请求写入共享属性包，ClarificationMiddleware 在 next 完成后读取
        _contextAccessor.Properties[ContextPropertyKeys.ClarificationRequest] = new ClarificationRequest
        {
            Question = question,
            Type = type,
            Context = context,
            Options = options
        };

        return $"[Clarification requested: {question}]";
    }
}

/// <summary>
/// 澄清请求数据（工具 → 中间件传递）
/// </summary>
public class ClarificationRequest
{
    public string Question { get; set; } = string.Empty;
    public ClarificationType Type { get; set; }
    public string? Context { get; set; }
    public List<string>? Options { get; set; }
}
