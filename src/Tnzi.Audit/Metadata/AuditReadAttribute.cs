
namespace Tnzi.Audit.Metadata;

/// <summary>
/// 标记端点为读语义（尽管 HTTP 方法是 POST 等写形态）：操作审计采集时归类为读——
/// 不出现在 Operations（变更类操作）视图，仍完整记录于 Logs（全量）视图。
/// <para>
/// 适用于无法用 <c>POST .../query</c> 路由惯例表达的伪读端点（export/preview/search 等），
/// 让审计分类与路由/方法名解耦。admin 面通常无需标记：仅类级 <c>.view</c> 门控的端点
/// 会经权限码信号自动归类为读（见 AuditOperationClassifier）。
/// </para>
/// 可标注于 Action（仅该端点）或 Controller（整个控制器的全部端点）。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AuditReadAttribute : Attribute;
