namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// AgentAttachment 实体配置类
/// </summary>
/// <remarks>
/// 没有外键：上传先于它所在的回复消息写入（工具图片在运行中途就落库，回复在运行结束时才写），
/// 删除由线程删除处理器与线程回退显式完成，与 <c>AgentArtifact</c> 同一形状。
/// </remarks>
public class AgentAttachmentConfiguration : EntityTypeConfigurationBase<AgentAttachment, Guid>
{
    public override void Configure(EntityTypeBuilder<AgentAttachment> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.MediaType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.FileName)
            .HasMaxLength(500);

        builder.Property(e => e.ToolCallId)
            .HasMaxLength(200);

        builder.Property(e => e.Content)
            .IsRequired();

        builder.HasIndex(e => e.ThreadId);
        builder.HasIndex(e => e.MessageId);
        builder.HasIndex(e => e.RunId);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }
    }
}
