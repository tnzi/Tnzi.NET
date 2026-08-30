namespace Tnzi.Storage.Workspace.Entities;

/// <summary>
/// FileFolder entity configuration
/// </summary>
public class FileFolderConfiguration : EntityTypeConfigurationBase<FileFolder, Guid>
{
    public override void Configure(EntityTypeBuilder<FileFolder> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        // 显式写死表名，与本模块其余六个配置一致。今天写不写效果相同 ——
        // 没有 ToTable 时 EF 拿 CLR 类名当基础表名，前缀器再拼成 Storage_FileFolder，
        // 与这里写的一字不差。写下来是为了把表名从「类名恰好没被改过」这件事上解绑：
        // 改一次类名就会静默改掉一张已有数据的表名，而没有任何测试会因此变红。
        builder.ToTable("FileFolder");

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.Path)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(e => e.Description)
            .HasMaxLength(1024);

        builder.Property(e => e.SortOrder)
            .HasDefaultValue(0);

        // Self-referential relationship
        builder.HasOne(e => e.Parent)
            .WithMany(e => e.Children)
            .HasForeignKey(e => e.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.ParentId })
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

            builder.HasIndex(e => new { e.TenantId, e.Path })
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }
        else
        {
            builder.HasIndex(e => e.ParentId)
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

            builder.HasIndex(e => e.Path)
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }
    }
}
