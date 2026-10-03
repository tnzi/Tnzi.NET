namespace Tnzi.AI.Cli.Entities.Configs;

/// <summary>
/// 外部运行时注册表配置。
/// </summary>
public class CliRuntimeConfiguration : EntityTypeConfigurationBase<CliRuntime, Guid>
{
    /// <inheritdoc />
    public override void Configure(EntityTypeBuilder<CliRuntime> builder)
    {
        builder.Property(e => e.HostId).IsRequired().HasMaxLength(200);
        builder.Property(e => e.ProviderKey).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.ExecutablePath).IsRequired().HasMaxLength(1000);
        builder.Property(e => e.CliVersion).HasMaxLength(200);

        // 同一宿主上同一个 provider 只注册一次：重复探测应当更新既有行而不是叠加。
        // 运行时是宿主级资源（见实体注释），多租户开关不改变这条约束的形状。
        builder.HasIndex(e => new { e.HostId, e.ProviderKey })
            .IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        builder.HasIndex(e => e.Status);
    }
}
