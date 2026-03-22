
namespace Tnzi.AI.Engine.Handoff;

/// <summary>
/// Handoff 执行策略配置
/// </summary>
public class HandoffConfiguration
{
    /// <summary>可转接的目标 Agent（显示名 → Agent ID）</summary>
    public Dictionary<string, Guid> Targets { get; set; } = new();

    /// <summary>最大转接次数（防止无限循环）</summary>
    public int MaxHandoffs { get; set; } = 10;

    /// <summary>是否允许目标 Agent 自动转回来源 Agent（默认 true）</summary>
    public bool AllowReturnToSource { get; set; } = true;
}
