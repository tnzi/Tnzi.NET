
namespace Tnzi.AI.Infrastructure.Engine;

/// <summary>
/// AgentExecutor 配置选项
/// </summary>
public class AgentExecutorOptions
{
    /// <summary>
    /// Agent 名称
    /// </summary>
    public string Name { get; set; } = "Agent";

    /// <summary>
    /// 系统指令
    /// </summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// AI 工具列表
    /// </summary>
    public IList<AITool>? Tools { get; set; }

    /// <summary>
    /// 温度参数
    /// </summary>
    public float? Temperature { get; set; }

    /// <summary>
    /// 最大输出 Token 数
    /// </summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>
    /// 最大工具调用迭代次数（防止无限循环）
    /// </summary>
    public int MaxToolIterations { get; set; } = 10;

    /// <summary>
    /// 单个工具调用超时时间（秒）
    /// </summary>
    public int ToolTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// 历史消息压缩器（可选）
    /// </summary>
    public IHistoryReducer? HistoryReducer { get; set; }

    /// <summary>
    /// 上下文注入提供者（可选）
    /// </summary>
    public IContextProvider? ContextProvider { get; set; }

    /// <summary>
    /// 工具执行中间件列表（按注册顺序执行，洋葱模型）
    /// </summary>
    public IReadOnlyList<IToolExecutionMiddleware>? Middlewares { get; set; }
}
