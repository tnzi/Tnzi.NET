namespace Tnzi.Storage.Workspace.Entities;

/// <summary>
/// RecycleBinEntry 实体配置
/// </summary>
public class RecycleBinEntryConfiguration : EntityTypeConfigurationBase<RecycleBinEntry, Guid>
{
    public override void Configure(EntityTypeBuilder<RecycleBinEntry> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        // 显式写死基础表名，理由同 FileFolderConfiguration：表名不该取决于 CLR 类名恰好没被改过。
        builder.ToTable("RecycleBinEntry");

        builder.Property(e => e.Name).IsRequired().HasMaxLength(256);
        builder.Property(e => e.OriginalLocation).IsRequired().HasMaxLength(2048);
        builder.Property(e => e.CreationTime).IsRequired();

        // 一个文件或目录同一时刻只能在回收站里出现一次。
        builder.HasIndex(e => new { e.ItemType, e.ItemId }).IsUnique();

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.OwnerId, e.CreationTime });
        }
        else
        {
            builder.HasIndex(e => new { e.OwnerId, e.CreationTime });
        }

        // 过期自动清除按放进来的时间扫。
        builder.HasIndex(e => e.CreationTime);
    }
}
