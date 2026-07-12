namespace Tnzi.Chat.Entities.Configs;

public class BroadcastLogConfiguration : EntityTypeConfigurationBase<BroadcastLog, Guid>
{
    public override void Configure(EntityTypeBuilder<BroadcastLog> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;
        if (multiTenancyEnabled) builder.HasIndex(b => b.TenantId);

        builder.Property(b => b.Content).HasMaxLength(4000).IsRequired();
        builder.Property(b => b.TargetSummary).HasMaxLength(200);
        builder.Property(b => b.Source).HasMaxLength(128);
        builder.HasIndex(b => b.CreationTime);
    }
}
