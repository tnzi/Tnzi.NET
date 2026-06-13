namespace Tnzi.AI.Dtos;

/// <summary>
/// 设置单条授权启用状态的请求体。Request body for toggling a single grant's enabled flag.
/// </summary>
public sealed class SetGrantEnabledDto
{
    /// <summary>目标启用状态。Target enabled state.</summary>
    public bool Enabled { get; init; }
}

/// <summary>
/// 设置单条授权优先级的请求体。Request body for setting a single grant's priority.
/// </summary>
public sealed class SetGrantPriorityDto
{
    /// <summary>优先级（越大越优先）。Priority (higher wins).</summary>
    public int Priority { get; init; }
}
