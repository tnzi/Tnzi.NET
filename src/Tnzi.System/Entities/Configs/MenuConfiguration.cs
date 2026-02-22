
namespace Tnzi.System.Entities.Configs;

/// <summary>
/// Menu 实体配置类
/// </summary>
public class MenuConfiguration : EntityTypeConfigurationBase<Menu, Guid>
{
    public override void Configure(EntityTypeBuilder<Menu> builder)
    {
        // 表名由 TableNamePrefix 属性自动处理
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => m.ParentId);
        builder.HasIndex(m => m.Path);
        builder.HasOne<Menu>()
            .WithMany()
            .HasForeignKey(m => m.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(m => m.Type)
            .HasDefaultValue(MenuType.Menu);
    }
}
