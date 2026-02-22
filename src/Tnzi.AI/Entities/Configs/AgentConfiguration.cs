namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// Agent 实体配置类
/// </summary>
public class AgentConfiguration : EntityTypeConfigurationBase<Agent, Guid>
{
    public override void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.Provider)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Model)
            .HasMaxLength(100);

        builder.Property(e => e.Instructions)
            .HasMaxLength(4000);

        builder.HasIndex(e => e.Name)
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        builder.HasIndex(e => e.IsEnabled);
    }
}
