namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// QueuedThreadMessage 实体配置类
/// </summary>
public class QueuedThreadMessageConfiguration : EntityTypeConfigurationBase<QueuedThreadMessage, Guid>
{
    public override void Configure(EntityTypeBuilder<QueuedThreadMessage> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Text)
            .IsRequired();

        builder.Property(e => e.Model)
            .HasMaxLength(QueuedThreadMessage.ModelMaxLength);
        builder.Property(e => e.Effort)
            .HasMaxLength(QueuedThreadMessage.EffortMaxLength);

        builder.Property(e => e.Failure)
            .HasMaxLength(QueuedThreadMessage.FailureMaxLength);

        builder.Property(e => e.FailureCode)
            .HasMaxLength(QueuedThreadMessage.FailureCodeMaxLength);

        // 线程是软删的，级联只在硬删时生效；软删的清理在 ThreadCleanupHandler
        builder.HasOne(e => e.Thread)
            .WithMany()
            .HasForeignKey(e => e.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }

        builder.HasIndex(e => e.ThreadId);
        builder.HasIndex(e => new { e.State, e.AfterRunId });
        builder.HasIndex(e => e.HandedToRunId);
    }
}
