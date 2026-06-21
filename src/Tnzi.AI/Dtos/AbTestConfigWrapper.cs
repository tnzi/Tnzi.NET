namespace Tnzi.AI.Dtos;

/// <summary>
/// A/B 测试配置包装（嵌入到 Agent.Configuration JSON）。
/// 仅由 <c>AgentVersionRouter</c> 用于反序列化 <see cref="AbTestConfig"/>。
/// </summary>
internal sealed class AbTestConfigWrapper
{
    [JsonPropertyName("abTest")]
    public AbTestConfig? AbTest { get; set; }
}
