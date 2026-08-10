namespace Tnzi.Signing.Entities.Configs;

/// <summary>EnvelopeTemplate 实体配置</summary>
public class EnvelopeTemplateConfiguration : EntityTypeConfigurationBase<EnvelopeTemplate, Guid>
{
    public override void Configure(EntityTypeBuilder<EnvelopeTemplate> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Category).HasMaxLength(80);
        builder.Property(t => t.HostEntityTypes).HasMaxLength(400);
        builder.Property(t => t.SourceFileName).HasMaxLength(260);

        if (multiTenancyEnabled) builder.HasIndex(t => t.TenantId);
        builder.HasIndex(t => t.Category);
        builder.HasIndex(t => t.Source);
        builder.HasIndex(t => t.Name).HasFilter(IndexFilterFactory.GetIsDeletedFalse());
    }
}

/// <summary>Field 实体配置</summary>
public class FieldConfiguration : EntityTypeConfigurationBase<Field, Guid>
{
    public override void Configure(EntityTypeBuilder<Field> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(f => f.Key).IsRequired().HasMaxLength(80);
        builder.Property(f => f.Label).IsRequired().HasMaxLength(200);
        builder.Property(f => f.RecipientRole).HasMaxLength(60);
        builder.Property(f => f.Binding).HasMaxLength(200);
        builder.Property(f => f.AnchorText).HasMaxLength(400);

        // 归一化坐标：0-1 之间，五位小数足够定位到亚毫米。
        builder.Property(f => f.X).HasPrecision(9, 5);
        builder.Property(f => f.Y).HasPrecision(9, 5);
        builder.Property(f => f.W).HasPrecision(9, 5);
        builder.Property(f => f.H).HasPrecision(9, 5);
        builder.Property(f => f.FontSize).HasPrecision(6, 2);

        builder.HasOne(f => f.Template)
            .WithMany(t => t.Fields)
            .HasForeignKey(f => f.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        if (multiTenancyEnabled) builder.HasIndex(f => f.TenantId);
        builder.HasIndex(f => f.TemplateId);
        // 硬删行，没有 IsDeleted 可过滤 —— 唯一约束是无条件的（同 BracketRow 先例）。
        builder.HasIndex(f => new { f.TemplateId, f.Key }).IsUnique();
    }
}

/// <summary>Envelope 实体配置</summary>
public class EnvelopeConfiguration : EntityTypeConfigurationBase<Envelope, Guid>
{
    public override void Configure(EntityTypeBuilder<Envelope> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(r => r.HostEntityType).HasMaxLength(60);
        builder.Property(r => r.Title).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Sha256).HasMaxLength(64);
        builder.Property(r => r.SentByName).HasMaxLength(160);

        if (multiTenancyEnabled) builder.HasIndex(r => r.TenantId);
        // 宿主查询（"这条记录上有哪些签署请求"）是最常走的一条路。
        builder.HasIndex(r => new { r.HostEntityType, r.HostEntityId });
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.TemplateId);
    }
}

/// <summary>Signer 实体配置</summary>
public class SignerConfiguration : EntityTypeConfigurationBase<Signer, Guid>
{
    public override void Configure(EntityTypeBuilder<Signer> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(r => r.Role).IsRequired().HasMaxLength(60);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(160);
        builder.Property(r => r.Email).HasMaxLength(256);
        builder.Property(r => r.TokenHash).HasMaxLength(64);
        builder.Property(r => r.DeclineReason).HasMaxLength(1000);
        builder.Property(r => r.SignerIp).HasMaxLength(64);
        builder.Property(r => r.SignerUserAgent).HasMaxLength(512);

        builder.HasOne(r => r.Request)
            .WithMany(q => q.Recipients)
            .HasForeignKey(r => r.RequestId)
            .OnDelete(DeleteBehavior.Cascade);

        if (multiTenancyEnabled) builder.HasIndex(r => r.TenantId);
        builder.HasIndex(r => r.RequestId);
        builder.HasIndex(r => r.Status);
        // 打开链接的那一次查询走这个索引；唯一是为了让一次令牌碰撞在写入时就炸，
        // 而不是变成两个人共用一条签署链接。
        // ★ 过滤器必须排除 NULL：草稿阶段收件人还没有令牌，若把它们一并纳入唯一约束，
        //   一份请求的第二个收件人就插不进去。
        builder.HasIndex(r => r.TokenHash)
            .IsUnique()
            .HasFilter(IndexFilterFactory.GetCodeNotNullAndIsDeletedFalse("TokenHash", "IsDeleted"));
    }
}

/// <summary>FieldValue 实体配置</summary>
public class FieldValueConfiguration : EntityTypeConfigurationBase<FieldValue, Guid>
{
    public override void Configure(EntityTypeBuilder<FieldValue> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(v => v.FieldKey).IsRequired().HasMaxLength(80);

        builder.HasOne(v => v.Request)
            .WithMany(r => r.FieldValues)
            .HasForeignKey(v => v.RequestId)
            .OnDelete(DeleteBehavior.Cascade);

        if (multiTenancyEnabled) builder.HasIndex(v => v.TenantId);
        builder.HasIndex(v => v.RequestId);
        builder.HasIndex(v => new { v.RequestId, v.FieldKey })
            .IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
    }
}
