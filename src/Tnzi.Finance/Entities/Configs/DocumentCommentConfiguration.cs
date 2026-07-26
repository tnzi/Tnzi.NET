namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 单据讨论配置
/// </summary>
public class DocumentCommentConfiguration : EntityTypeConfigurationBase<DocumentComment, Guid>
{
    public override void Configure(EntityTypeBuilder<DocumentComment> builder)
    {
        builder.Property(e => e.SourceType).HasMaxLength(64).IsRequired();
        builder.Property(e => e.SourceId).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Body).HasMaxLength(4000).IsRequired();

        builder.HasIndex(e => new { e.SourceType, e.SourceId });
    }
}
