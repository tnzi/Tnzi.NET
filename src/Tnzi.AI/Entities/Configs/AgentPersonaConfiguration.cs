namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// AgentPersona 实体配置类。
/// 镜像 SkillEntityConfiguration 的 ResourceScope 模式 — 不使用 IMultiTenant，
/// 可见性通过 Scope + TenantId 组合在服务层强制执行。
/// </summary>
public class AgentPersonaConfiguration : EntityTypeConfigurationBase<AgentPersona, Guid>
{
    public override void Configure(EntityTypeBuilder<AgentPersona> builder)
    {
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Slug)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Content)
            .IsRequired()
            .HasMaxLength(32000);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.Scope)
            .IsRequired();

        // 唯一索引：同一作用域+租户下的 Slug 唯一（软删除行排除，与 Provider 的过滤索引一致）。
        builder.HasIndex(e => new { e.Slug, e.Scope, e.TenantId })
            .IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        // 租户查询索引
        builder.HasIndex(e => new { e.Scope, e.TenantId });
    }
}
