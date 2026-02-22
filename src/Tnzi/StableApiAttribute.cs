namespace Tnzi;

/// <summary>
/// 标记为稳定 API — 遵循语义化版本控制，主版本号不变不会有破坏性变更
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
public sealed class StableApiAttribute : Attribute
{
    /// <summary>
    /// 从哪个版本开始稳定
    /// </summary>
    public string? Since { get; set; }
}
