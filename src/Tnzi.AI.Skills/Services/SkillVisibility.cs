namespace Tnzi.AI.Skills.Services;

/// <summary>
/// 管理面读技能行时的租户可见性。<see cref="SkillEntity"/> 是 <see cref="IScopedResource"/>，没有全局多租户过滤器
/// （System 行要对每个租户可见），所以每条管理面读路径都要显式套这一层；技能服务与分类服务共用这一份。
/// </summary>
internal static class SkillVisibility
{
    /// <summary>
    /// 单租户（<paramref name="tenantId"/> 为 null）不过滤；多租户只留 System 行与本租户的 Tenant / User 行。
    /// </summary>
    public static IQueryable<SkillEntity> ForTenant(IQueryable<SkillEntity> query, Guid? tenantId)
    {
        if (tenantId == null)
            return query;

        return query.Where(e =>
            e.Scope == SkillScope.System ||
            (e.Scope == SkillScope.Tenant && e.TenantId == tenantId) ||
            (e.Scope == SkillScope.User && e.TenantId == tenantId));
    }
}
