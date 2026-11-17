namespace Tnzi.Identity.OpenIdConnect.Entities.Configs;

/// <summary>
/// <see cref="OidcClient"/> 的 EF 映射：先套 OpenIddict 自己的映射（键、并发令牌、client_id 唯一索引、导航），
/// 再加本模块的三列。
/// </summary>
public class OidcClientConfiguration : EntityTypeConfigurationBase<OidcClient, Guid>
{
    /// <inheritdoc />
    public override void Configure(EntityTypeBuilder<OidcClient> builder)
    {
        new OpenIddictEntityFrameworkCoreApplicationConfiguration<OidcClient, OidcAuthorization, OidcToken, Guid>().Configure(builder);

        builder.Property(c => c.IsEnabled).IsRequired();
        builder.Property(c => c.ExternalReference).HasMaxLength(OidcClient.ExternalReferenceMaxLength);
        builder.Property(c => c.Description).HasMaxLength(OidcClient.DescriptionMaxLength);
        builder.HasIndex(c => c.ExternalReference);
    }
}
