namespace Tnzi.AI.Prompt;

/// <summary>
/// 系统提示词模板工具 — 将 Agent 静态 Instructions 包裹为 &lt;instructions&gt; XML 段落。
/// </summary>
/// <remarks>
/// 历史上此类支持 13 个 XML tag 段落与 ISystemPromptSectionProvider 动态装配管线，
/// 但全仓唯一生产消费者是 <see cref="Engine.AgentExecutor"/> 的单一 instructions 包装；
/// soul/profile/memory 等动态注入实际走 ContextInjectionMiddleware。
/// 2026-06 死契约清理后收敛为最小静态工具（输出格式与旧装配管线逐字节一致）。
/// </remarks>
public static class SystemPromptTemplateBuilder
{
    /// <summary>instructions 段落 tag 名</summary>
    public const string InstructionsTag = "instructions";

    /// <summary>
    /// 将非空白 instructions 包裹为 &lt;instructions&gt; 段落；null/空白输入返回 null。
    /// </summary>
    public static string? WrapInstructions(string? instructions)
    {
        if (string.IsNullOrWhiteSpace(instructions))
        {
            return null;
        }

        return $"<{InstructionsTag}>\n{instructions}\n</{InstructionsTag}>";
    }
}
