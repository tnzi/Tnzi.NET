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

    /// <summary>
    /// 订阅定义源的变更（文件被改 / 新建 / 删除）。回调只是「有东西变了」的信号，不带内容；
    /// 订阅方（<c>AgentDefinitionSyncService</c>）据此重跑一次全量同步。
    /// 默认实现不通知（静态定义源），返回的句柄 Dispose 即退订。
    /// </summary>
    IDisposable OnDefinitionsChanged(Action callback) => NoopSubscription.Instance;

    /// <summary>不通知的占位订阅</summary>
    sealed class NoopSubscription : IDisposable
    {
        public static readonly NoopSubscription Instance = new();
        public void Dispose() { }
    }
}
