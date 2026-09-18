namespace Tnzi.Audit.Tests.Integration;

/// <summary>
/// 带租户维度的保留策略被试实体。
/// </summary>
/// <remarks>
/// <see cref="RetentionTestRecord"/> 刻意不带租户（守「不实现 IMultiTenant 的实体照常跑一次」），
/// 所以按租户逐个执行那条路 —— 切进每个租户销毁、证书记在被销毁数据所属的租户名下 ——
/// 在它身上根本走不到。本实体只在开启多租户的测试 DbContext 里有意义。
/// </remarks>
public class TenantRetentionTestRecord : MultiTenantAuditedEntity<Guid>
{
    /// <summary>业务分类，与 <see cref="RetentionTestRecord"/> 同形。</summary>
    public string Category { get; set; } = string.Empty;
}
