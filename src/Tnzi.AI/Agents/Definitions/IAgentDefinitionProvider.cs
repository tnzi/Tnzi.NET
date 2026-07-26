namespace Tnzi.AI.Agents.Definitions;

/// <summary>
/// Agent 定义提供器接口 - 从文件系统加载 Agent 定义
/// </summary>
public interface IAgentDefinitionProvider
{
    /// <summary>
    /// 加载所有 Agent 定义
    /// </summary>
    Task<IReadOnlyList<AgentDefinitionDto>> LoadDefinitionsAsync(CancellationToken ct = default);

    /// <summary>
    /// 按名称获取 Agent 定义
    /// </summary>
    Task<AgentDefinitionDto?> GetByNameAsync(string name, CancellationToken ct = default);
}
