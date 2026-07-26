namespace Tnzi.Chat.Entities.Configs;

public class ConversationConfiguration : EntityTypeConfigurationBase<Conversation, Guid>
{
    public override void Configure(EntityTypeBuilder<Conversation> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;
        if (multiTenancyEnabled) builder.HasIndex(c => c.TenantId);

        builder.Property(c => c.Title).HasMaxLength(200);
        builder.Property(c => c.AvatarFileId).HasMaxLength(256);
        builder.Property(c => c.Notice).HasMaxLength(2000);
        builder.Property(c => c.DirectKey).HasMaxLength(128);
        builder.Property(c => c.LastMessagePreview).HasMaxLength(200);

        // DirectKey 唯一索引必须同时排除 NULL 行与软删除行：
        // ① Group 会话的 DirectKey 恒为 null，无 NULL 过滤时 SQL Server 的唯一索引只容一个
        //    NULL —— 全库只能存在一个群；
        // ② Conversation 带软删除（MultiTenantAuditedEntity），无 IsDeleted 过滤时被软删的
        //    Direct/System 会话会永久占用其 DirectKey，同一对用户再开聊 / 再发系统通知时
        //    查询（带软删过滤）查不到旧行 → INSERT 撞唯一约束 → 500。
        var directKeyFilter = IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("DirectKey");
        if (multiTenancyEnabled)
            builder.HasIndex(c => new { c.TenantId, c.DirectKey }).IsUnique().HasFilter(directKeyFilter);
        else
            builder.HasIndex(c => c.DirectKey).IsUnique().HasFilter(directKeyFilter);
        builder.HasIndex(c => new { c.Type, c.LastMessageAt });
    }
}
