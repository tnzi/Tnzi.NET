
namespace Tnzi.System.Entities.Configs;

/// <summary>
/// Setting 实体配置类
/// </summary>
public class SettingConfiguration : EntityTypeConfigurationBase<Setting, Guid>
{
    public override void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.Property(s => s.Key)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.Value)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(s => s.Description)
            .HasMaxLength(500);

        builder.Property(s => s.Group)
            .HasMaxLength(50);

        builder.Property(s => s.ValueType)
            .HasDefaultValue(SettingValueType.String);

        builder.Property(s => s.Scope)
            .HasDefaultValue(SettingScope.Global);

        builder.Property(s => s.ScopeId)
            .HasMaxLength(64);

        // 创建唯一索引（Key + Scope + ScopeId，包含 IsDeleted 过滤）
        builder.HasIndex(s => new { s.Key, s.Scope, s.ScopeId })
            .IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        // 创建分组索引（用于查询）
        builder.HasIndex(s => s.Group);

        // 创建作用域索引（用于按 Scope+ScopeId 查询）
        builder.HasIndex(s => new { s.Scope, s.ScopeId });
    }
}
