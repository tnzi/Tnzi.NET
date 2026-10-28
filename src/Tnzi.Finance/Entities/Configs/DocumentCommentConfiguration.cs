namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 单据讨论配置
/// </summary>
public class DocumentCommentConfiguration : EntityTypeConfigurationBase<DocumentComment, Guid>
{
    public override void Configure(EntityTypeBuilder<DocumentComment> builder)
    {
        builder.Property(e => e.SourceType).HasMaxLength(FinanceFieldLimits.DocumentKeyMaxLength).IsRequired();
        builder.Property(e => e.SourceId).HasMaxLength(FinanceFieldLimits.DocumentKeyMaxLength).IsRequired();
        builder.Property(e => e.Body).HasMaxLength(FinanceFieldLimits.CommentBodyMaxLength).IsRequired();

        builder.HasIndex(e => new { e.SourceType, e.SourceId });
    }
}
