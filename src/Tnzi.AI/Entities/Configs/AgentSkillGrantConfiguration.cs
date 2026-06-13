namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// AgentSkillGrant 实体配置类
/// </summary>
public class AgentSkillGrantConfiguration : EntityTypeConfigurationBase<AgentSkillGrant, Guid>
{
    public override void Configure(EntityTypeBuilder<AgentSkillGrant> builder)
    {
        builder.Property(e => e.SkillSlug)
            .IsRequired()
            .HasMaxLength(200);

        // FK 仅指向同程序集的 Agent（Agent 不暴露反向集合，故 WithMany() 无参）。
        builder.HasOne(e => e.Agent)
            .WithMany()
            .HasForeignKey(e => e.AgentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.AgentId);

        // 同一 Agent 下 SkillSlug 唯一（软删除行排除）。
        builder.HasIndex(e => new { e.AgentId, e.SkillSlug })
            .IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        // 反向查询索引（按 slug 查哪些 Agent 被授权）。
        builder.HasIndex(e => e.SkillSlug);
    }
}
