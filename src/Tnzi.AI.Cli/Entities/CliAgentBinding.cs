namespace Tnzi.AI.Cli.Entities;

/// <summary>
/// 把框架 Agent 绑定到一个外部运行时，并携带 CLI 特有配置。
/// </summary>
/// <remarks>
/// <b>刻意不改核心 <c>Agent</c> 实体</b>：外部执行是可选子模块的能力，往核心实体加一列
/// 会让每个消费应用都为自己不用的功能补一次迁移。「这个 Agent 走不走外部」由本表的
/// 行是否存在来表达 —— 一条记录即一个开关，删除即回到内建执行。
/// </remarks>
public class CliAgentBinding : MultiTenantAuditedEntity<Guid>
{
    /// <summary>框架 Agent ID。唯一 —— 一个 Agent 至多一个外部绑定。</summary>
    public Guid AgentId { get; set; }

    /// <summary>目标外部运行时。</summary>
    public Guid CliRuntimeId { get; set; }

    /// <summary>模型覆盖。空 = 用 CLI 自己的默认。</summary>
    public string? Model { get; set; }

    /// <summary>
    /// 运行时原生的推理强度值，原样往返。
    /// </summary>
    /// <remarks>
    /// 刻意<b>不</b>压平成跨 provider 的统一枚举：各家的档位既不同名也不同数量
    /// （某些是 low/medium/high/xhigh/max，另一些是 none/…/ultra，还有的是模型变体名），
    /// 映射到公共枚举必然产生不可逆的信息损失，而损失掉的恰恰是用户显式选的那一档。
    /// </remarks>
    public string? ThinkingLevel { get; set; }

    /// <summary>每 agent 自定义 CLI 参数（JSON 数组）。</summary>
    public string? CustomArgsJson { get; set; }

    /// <summary>受管 MCP 配置 JSON。null = 继承宿主本机配置。</summary>
    public string? McpConfigJson { get; set; }

    /// <summary>工作目录策略。</summary>
    public CliWorkDirectoryMode WorkDirectoryMode { get; set; } = CliWorkDirectoryMode.PerThread;

    /// <summary><see cref="WorkDirectoryMode"/> = UserProvided 时的绝对路径。</summary>
    public string? UserWorkDirectory { get; set; }

    /// <summary>是否把 Agent 的 SystemPrompt 写进 brief。</summary>
    public bool InjectAgentInstructions { get; set; } = true;

    /// <summary>是否把 Agent 授予的 skills 物化到 provider 原生目录。</summary>
    public bool MaterializeSkills { get; set; } = true;

    /// <summary>
    /// 本 agent 的空闲看门狗覆盖。<b>只允许收紧，不允许放宽</b> ——
    /// 超过全局配置值时按全局值执行（否则每个 agent 都能自己解除全局安全边界）。
    /// </summary>
    public TimeSpan? IdleWatchdog { get; set; }
}
