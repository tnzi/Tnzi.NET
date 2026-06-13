namespace Tnzi.AI.Enums;

/// <summary>
/// 共享资源实体的可见性作用域（Provider、AgentPersona 等）。
/// 泛化了 SkillScope。这些实体有意地不实现 IMultiTenant（全局查询过滤器
/// 没有共享行子句，System 行对每个真实租户不可见）；可见性在服务层强制执行：
/// 租户可看到 System ∪ 自己的 Tenant 行。
/// </summary>
public enum ResourceScope
{
    /// <summary>全局系统级（所有租户可见）</summary>
    System = 0,

    /// <summary>租户私有（仅所属租户可见）</summary>
    Tenant = 1,

    /// <summary>用户私有（仅所属用户可见）</summary>
    User = 2
}
