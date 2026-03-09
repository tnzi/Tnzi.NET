namespace Tnzi.AI.Rag.Entities.Configs;

/// <summary>
/// KnowledgeDocument 实体配置
/// </summary>
public class KnowledgeDocumentConfiguration : EntityTypeConfigurationBase<KnowledgeDocument, Guid>
{
    public override Type? DbContextType => typeof(RagDbContext);

    public override void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.FileName)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.ContentType)
            .HasMaxLength(200);

        builder.Property(e => e.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(e => e.ContentHash)
            .HasMaxLength(64);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }

        builder.HasIndex(e => e.KnowledgeBaseId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => new { e.KnowledgeBaseId, e.ContentHash });
    }
}
