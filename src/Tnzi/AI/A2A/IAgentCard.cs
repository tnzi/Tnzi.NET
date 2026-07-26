namespace Tnzi.AI.A2A;

/// <summary>
/// Agent 名片接口 - 描述 Agent 的能力和端点信息
/// </summary>
[ExperimentalApi(Reason = "A2A protocol is in preview")]
public interface IAgentCard
{
    /// <summary>
    /// Agent 名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Agent 描述
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Agent 能力列表
    /// </summary>
    List<string> Capabilities { get; }

    /// <summary>
    /// Agent 端点地址
    /// </summary>
    string Endpoint { get; }

    /// <summary>
    /// 扩展元数据
    /// </summary>
    Dictionary<string, string> Metadata { get; }
}

/// <summary>
/// Agent 名片默认实现
/// </summary>
[ExperimentalApi(Reason = "A2A protocol is in preview")]
public class AgentCard : IAgentCard
{
    /// <inheritdoc />
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc />
    public string Description { get; set; } = string.Empty;

    /// <inheritdoc />
    public List<string> Capabilities { get; set; } = [];

    /// <inheritdoc />
    public string Endpoint { get; set; } = string.Empty;

    /// <inheritdoc />
    public Dictionary<string, string> Metadata { get; set; } = new();
}
