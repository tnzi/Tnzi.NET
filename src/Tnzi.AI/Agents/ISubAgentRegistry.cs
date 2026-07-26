namespace Tnzi.AI.Agents;

/// <summary>
/// 子 Agent 类型注册表 - 管理可用的子 Agent 类型定义
/// </summary>
public interface ISubAgentRegistry
{
    /// <summary>获取所有已注册的子 Agent 类型</summary>
    IReadOnlyList<SubAgentTypeDefinition> GetAll();

    /// <summary>按名称获取子 Agent 类型（不存在返回 null）</summary>
    SubAgentTypeDefinition? Get(string name);

    /// <summary>注册自定义子 Agent 类型（同名覆盖）</summary>
    void Register(SubAgentTypeDefinition definition);

    /// <summary>取消注册子 Agent 类型</summary>
    bool Unregister(string name);

    /// <summary>从数据库加载已启用的子 Agent 类型定义（合并到内存注册表，同名覆盖内置类型）</summary>
    Task LoadFromStoreAsync(IRepository<SubAgentType, Guid> repository, CancellationToken cancellationToken = default);
}
