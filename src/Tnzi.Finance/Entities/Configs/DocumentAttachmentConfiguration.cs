namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 单据附件配置
/// </summary>
public class DocumentAttachmentConfiguration : EntityTypeConfigurationBase<DocumentAttachment, Guid>
{
    public override void Configure(EntityTypeBuilder<DocumentAttachment> builder)
    {
        builder.Property(e => e.SourceType).HasMaxLength(64).IsRequired();
        builder.Property(e => e.SourceId).HasMaxLength(64).IsRequired();
        builder.Property(e => e.FileName).HasMaxLength(260).IsRequired();
        builder.Property(e => e.ContentType).HasMaxLength(128);
        builder.Property(e => e.Caption).HasMaxLength(500);

        // 读取形态永远是"某张单据的全部附件"。
        builder.HasIndex(e => new { e.SourceType, e.SourceId });
        builder.HasIndex(e => e.FileId);
    }
}
