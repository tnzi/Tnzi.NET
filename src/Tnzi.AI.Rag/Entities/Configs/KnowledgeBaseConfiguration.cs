namespace Tnzi.AI.Rag.Entities.Configs;

/// <summary>
/// KnowledgeBase 实体配置
/// </summary>
public class KnowledgeBaseConfiguration : EntityTypeConfigurationBase<KnowledgeBase, Guid>
{
    public override Type? DbContextType => typeof(RagDbContext);

    public override void Configure(EntityTypeBuilder<KnowledgeBase> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.EmbeddingProvider)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.EmbeddingModel)
            .HasMaxLength(200);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId)
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        builder.HasIndex(e => e.Name)
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        builder.HasIndex(e => e.IsEnabled)
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
    }
}
