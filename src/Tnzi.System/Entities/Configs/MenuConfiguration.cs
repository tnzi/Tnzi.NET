
namespace Tnzi.System.Entities.Configs;

/// <summary>
/// Menu 实体配置类
/// </summary>
public class MenuConfiguration : EntityTypeConfigurationBase<Menu, Guid>
{
    public override void Configure(EntityTypeBuilder<Menu> builder)
    {
        builder.HasIndex(m => m.ParentId);
        builder.HasIndex(m => m.Path);
        builder.HasOne<Menu>()
            .WithMany()
            .HasForeignKey(m => m.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(m => m.Name).HasMaxLength(50);
        builder.Property(m => m.Icon).HasMaxLength(100);
        builder.Property(m => m.Path).HasMaxLength(200);
        builder.Property(m => m.Component).HasMaxLength(200);
        builder.Property(m => m.Permission).HasMaxLength(100);

        builder.Property(m => m.Type)
            .HasDefaultValue(MenuType.Menu);
    }
}
