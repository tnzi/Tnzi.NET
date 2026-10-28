namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 单据附件配置
/// </summary>
public class DocumentAttachmentConfiguration : EntityTypeConfigurationBase<DocumentAttachment, Guid>
{
    public override void Configure(EntityTypeBuilder<DocumentAttachment> builder)
    {
        builder.Property(e => e.SourceType).HasMaxLength(FinanceFieldLimits.DocumentKeyMaxLength).IsRequired();
        builder.Property(e => e.SourceId).HasMaxLength(FinanceFieldLimits.DocumentKeyMaxLength).IsRequired();
        builder.Property(e => e.FileName).HasMaxLength(FinanceFieldLimits.AttachmentFileNameMaxLength).IsRequired();
        builder.Property(e => e.ContentType).HasMaxLength(FinanceFieldLimits.AttachmentContentTypeMaxLength);
        builder.Property(e => e.Caption).HasMaxLength(FinanceFieldLimits.AttachmentCaptionMaxLength);

        // 读取形态永远是"某张单据的全部附件"。
        builder.HasIndex(e => new { e.SourceType, e.SourceId });
        builder.HasIndex(e => e.FileId);
    }
}
