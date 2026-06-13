namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// AgentKnowledgeGrant 实体配置类
/// </summary>
public class AgentKnowledgeGrantConfiguration : EntityTypeConfigurationBase<AgentKnowledgeGrant, Guid>
{
    public override void Configure(EntityTypeBuilder<AgentKnowledgeGrant> builder)
    {
        // FK 仅指向同程序集的 Agent（Agent 不暴露反向集合，故 WithMany() 无参）。
        // KnowledgeBaseId 是按值引用的裸 Guid，跨模块（Tnzi.AI.Rag），无 FK。
        builder.HasOne(e => e.Agent)
            .WithMany()
            .HasForeignKey(e => e.AgentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.AgentId);

        // 同一 Agent 下 KnowledgeBaseId 唯一（软删除行排除）。
        builder.HasIndex(e => new { e.AgentId, e.KnowledgeBaseId })
            .IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        // 反向查询索引（按知识库 Id 查哪些 Agent 被授权）。
        builder.HasIndex(e => e.KnowledgeBaseId);
    }
}
