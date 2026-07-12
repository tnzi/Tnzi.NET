namespace Tnzi;

/// <summary>
/// 标记为实验性 API：可能在任何版本中变更，使用时需注意。
/// </summary>
/// <remarks>
/// 与 <see cref="StableApiAttribute"/> 对称：本特性声明「不稳定意图」，提示使用者该 API 可能变更；
/// 但它<b>不</b>让该 API 免除 <c>PublicApiAnalyzers</c> 机器门禁。实验性 API 同样登记在
/// <c>PublicAPI.Shipped.txt</c> / <c>PublicAPI.Unshipped.txt</c> 中，变更时照常更新基线
/// （把条目从 Unshipped 滚入 Shipped，或对移除写 <c>*REMOVED*</c> 项）。意图归意图，事实校验归 analyzer。
/// 详见 docs/coding-standards/public-api-governance.md。
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
public sealed class ExperimentalApiAttribute : Attribute
{
    /// <summary>
    /// 说明实验性原因或替代方案
    /// </summary>
    public string? Reason { get; set; }
}
