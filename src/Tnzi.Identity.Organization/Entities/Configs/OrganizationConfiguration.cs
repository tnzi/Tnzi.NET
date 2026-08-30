namespace Tnzi.Identity.Organization.Entities;

/// <summary>
/// Organization 实体配置类
/// </summary>
public class OrganizationConfiguration : EntityTypeConfigurationBase<Organization, Guid>
{
    public override void Configure(EntityTypeBuilder<Organization> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        // 表名由 TableNamePrefix 属性自动处理
        builder.HasKey(o => o.Id);

        // 属性配置
        builder.Property(o => o.Name).IsRequired().HasMaxLength(200);
        builder.Property(o => o.Code).HasMaxLength(50);
        builder.Property(o => o.Remark).HasMaxLength(500);
        builder.Property(o => o.Path).HasMaxLength(2000);
        builder.Property(o => o.SortOrder).IsRequired().HasDefaultValue(0);
        builder.Property(o => o.IsEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(o => o.Level).IsRequired().HasDefaultValue(0);

        // 关系配置
        builder.HasOne(o => o.Parent)
            .WithMany(o => o.Children)
            .HasForeignKey(o => o.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // ★ User → Organization 的外键从**本侧**声明。
        //
        // 拆分前它写在 UserConfiguration 里（`HasOne(u => u.Organization)`），那是一条
        // 「父 → 子」的引用：核心的 User 实体认识本模块的 Organization。留着它，本模块就永远
        // 拆不出去，所以导航属性 `User.Organization` 一并删掉了。
        //
        // 从主体侧用 `HasMany<User>().WithOne()` 重新声明，得到的关系模型与拆分前**逐字相同**：
        // 外键仍然落在 User 上、列仍是 OrganizationId、删除行为仍是 SetNull，
        // 约束名仍由 EF 按「FK_{依赖表}_{主体表}_{列}」算（两侧声明算出来的名字一致，
        // 所以**刻意不写 HasConstraintName** —— 手写一个名字反而会在那些表名不是默认值的
        // 宿主上凭空造出一次 rename）。
        //
        // 不加载本模块时：User.OrganizationId 只是一个带索引的可空 Guid 列，没有外键约束
        // （已有库里物理约束仍在，只是模型不再描述它）。少一道数据库级的引用完整性，
        // 而由 UserService 在写入前校验组织是否存在来补位 —— 缺席只降低能力，不改变行为。
        builder.HasMany<User>()
            .WithOne()
            .HasForeignKey(u => u.OrganizationId)
            .OnDelete(DeleteBehavior.SetNull);

        // 索引配置（使用无参数方法自动检测数据库提供者，确保跨数据库兼容性）
        if (multiTenancyEnabled)
        {
            builder.HasIndex(o => new { o.TenantId, o.Code })
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetCodeNotNullAndIsDeletedFalse());
            builder.HasIndex(o => o.TenantId);
        }
        else
        {
            builder.HasIndex(o => o.Code)
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetCodeNotNullAndIsDeletedFalse());
        }

        builder.HasIndex(o => o.ParentId);
        builder.HasIndex(o => o.Path);
    }
}
