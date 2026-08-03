namespace Tnzi.AI.Cli.Services;

/// <summary>
/// 组装写进 provider 记忆文件的<b>稳定</b> brief。
/// </summary>
/// <remarks>
/// <para>
/// 「稳定」是硬要求，不是修辞：brief 落在 provider 的缓存前缀里（整段对话之前），
/// 内容一变就作废整个历史的 prompt cache，续接一次的成本按整段上下文重算。
/// 所以这里<b>只能</b>放随 Agent 定义变化的内容 —— 人格、指令、平台契约 ——
/// 绝不能出现时间戳、运行 ID、触发者这类每轮都变的东西（那些走
/// <see cref="CliRunContext.PerTurnContext"/>）。
/// </para>
/// <para>
/// 约定测试 <c>CliBriefComposerTests</c> 钉死这一点：同一个 Agent 组装两次必须逐字节相同。
/// </para>
/// </remarks>
public interface ICliBriefComposer
{
    /// <summary>为一个 Agent 组装稳定 brief。</summary>
    string Compose(Agent agent, CliProviderDescriptor provider);
}

/// <inheritdoc cref="ICliBriefComposer" />
public class CliBriefComposer : ICliBriefComposer
{
    /// <inheritdoc />
    public string Compose(Agent agent, CliProviderDescriptor provider)
    {
        Check.NotNull(agent);
        Check.NotNull(provider);

        var builder = new StringBuilder();

        builder.Append("# ").Append(agent.Name).Append('\n');

        if (!string.IsNullOrWhiteSpace(agent.Description))
        {
            builder.Append('\n').Append(agent.Description.Trim()).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(agent.Persona))
        {
            builder.Append("\n## Persona\n\n").Append(agent.Persona.Trim()).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(agent.Instructions))
        {
            builder.Append("\n## Instructions\n\n").Append(agent.Instructions.Trim()).Append('\n');
        }

        builder.Append("\n## Runtime contract\n\n");
        builder.Append(
            "You are running as a managed agent. The working directory is prepared for this run; "
            + "write deliverables into it. Do not ask interactive questions - there is no human at "
            + "the terminal and an unanswered prompt stalls the run until it is killed. "
            + "State conclusions explicitly at the end of your final message: everything after the "
            + "last tool call is what the caller receives.\n");

        return builder.ToString();
    }
}
