namespace Tnzi.Identity.Presence.Entities.Configs;

public class UserPresenceConfiguration : EntityTypeConfigurationBase<UserPresence, Guid>
{
    public override void Configure(EntityTypeBuilder<UserPresence> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;
        if (multiTenancyEnabled)
        {
            builder.HasIndex(p => p.TenantId);
            builder.HasIndex(p => new { p.TenantId, p.UserId }).IsUnique();
        }
        else
        {
            builder.HasIndex(p => p.UserId).IsUnique().HasFilter(null);
        }
    }
}
