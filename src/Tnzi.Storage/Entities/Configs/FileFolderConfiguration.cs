namespace Tnzi.Storage.Entities.Configs;

/// <summary>
/// FileFolder entity configuration
/// </summary>
public class FileFolderConfiguration : EntityTypeConfigurationBase<FileFolder, Guid>
{
    public override void Configure(EntityTypeBuilder<FileFolder> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

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
