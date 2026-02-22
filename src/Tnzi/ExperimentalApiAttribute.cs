namespace Tnzi;

/// <summary>
/// 标记为实验性 API — 可能在任何版本中变更，使用时需注意
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
public sealed class ExperimentalApiAttribute : Attribute
{
    /// <summary>
    /// 说明实验性原因或替代方案
    /// </summary>
    public string? Reason { get; set; }
}
