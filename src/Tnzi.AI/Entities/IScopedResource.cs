namespace Tnzi.AI.Entities;

/// <summary>
/// 标记接口：采用"共享资源 Scope 模式"的实体（System 共享 + Tenant 私有）。
/// 此类实体故意不实现 <see cref="IMultiTenant"/>（全局查询过滤器无共享行子句，会隐藏 System 行），
/// 改由 Scope + TenantId 在服务层强制可见性。架构测试 EntityPolicyTests 强制此约束。
/// </summary>
public interface IScopedResource
{
    ResourceScope Scope { get; }
    Guid? TenantId { get; }
}
