namespace Tnzi.AI.Dtos;

/// <summary>
/// A/B 测试配置包装（嵌入到 Agent.Configuration JSON）。
/// 引擎实现细节 — 仅由 <c>AgentVersionRouter</c> 用于反序列化 <see cref="AbTestConfig"/>，
/// 故随引擎实现保留在 <c>Tnzi.AI.Agent</c> 程序集（命名空间仍归属 <c>Tnzi.AI.Dtos</c>）。
/// </summary>
internal sealed class AbTestConfigWrapper
{
    [JsonPropertyName("abTest")]
    public AbTestConfig? AbTest { get; set; }
}
