namespace Tnzi.AI.Cli.Entities.Configs;

/// <summary>
/// 外部执行事件流配置。
/// </summary>
public class CliRunMessageConfiguration : EntityTypeConfigurationBase<CliRunMessage, Guid>
{
    /// <inheritdoc />
    public override void Configure(EntityTypeBuilder<CliRunMessage> builder)
    {
        builder.Property(e => e.Tool).HasMaxLength(200);
        builder.Property(e => e.CallId).HasMaxLength(200);
        builder.Property(e => e.Status).HasMaxLength(64);
        builder.Property(e => e.Level).HasMaxLength(32);

        // (RunId, Sequence) 唯一：断线重连按 Sequence 补发，序号重复会让客户端
        // 收到两条「同一位置」的事件而无法判断哪条是真的。
        // 本表只追加不软删，因此无需过滤器。
        builder.HasIndex(e => new { e.RunId, e.Sequence }).IsUnique();

        builder.HasIndex(e => e.TenantId);
    }
}
