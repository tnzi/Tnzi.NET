
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

        // Value 不设长度上限：除普通短设置外，还复用于存储 Appearance 主题快照等大 JSON
        // 文档（64KB 契约）。设 HasMaxLength(2000) 会在 SQL Server/PostgreSQL 上截断，
        // 故省略长度映射为 nvarchar(max)/text（不用库特定 HasColumnType）。
        builder.Property(s => s.Value)
            .IsRequired();

        builder.Property(s => s.Description)
            .HasMaxLength(500);

        builder.Property(s => s.Group)
            .HasMaxLength(50);

        builder.Property(s => s.IsEncrypted)
            .HasDefaultValue(false);

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
