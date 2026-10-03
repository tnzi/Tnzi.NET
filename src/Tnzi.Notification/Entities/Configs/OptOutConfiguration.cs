namespace Tnzi.Notification.Entities.Configs;

/// <summary>
/// OptOut 实体配置类
/// </summary>
public class OptOutConfiguration : EntityTypeConfigurationBase<OptOut, Guid>
{
    public override void Configure(EntityTypeBuilder<OptOut> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(o => o.Address).IsRequired().HasMaxLength(500);
        builder.Property(o => o.Category).HasMaxLength(100);
        builder.Property(o => o.Source).HasMaxLength(200);
        builder.Property(o => o.Reason).HasMaxLength(1000);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(o => o.TenantId);
        }

        // 发送前的判定按 (地址, 渠道) 查，这是热路径上唯一的一次查询。
        builder.HasIndex(o => new { o.Address, o.Channel });

        // 同一 (地址, 渠道, 分类) 只需要一条记录。二次退订是幂等的 upsert，
        // 不是又插一行 —— 否则一个反复点退订链接的收件人会在表里堆出几十行，
        // 而它们表达的是同一件事。服务层的判重是 read-then-write，两个并发请求都会
        // 读到"还没有"，所以最终把关的必须是数据库。
        // ★ 拆成两条，因为 Category 为 NULL 表示「整渠道退订」——不是"碰巧没填"，
        //   而是这张表里最要紧的那个档位。PostgreSQL / SQLite 认为唯一索引里的 NULL
        //   互不相等，所以单一条 (Address, Channel, Category) 唯一索引**挡不住**同一个
        //   地址被记两次整渠道退订；而 OptInAsync 恢复订阅时只删它查到的那一行，
        //   于是页面说"已恢复"、人却依然收不到邮件。SQL Server 上同样的代码是对的，
        //   这正是这类缺陷最擅长的伪装。
        // ★ 多租户时唯一性必须按租户划分：退订名单是租户自己的账本，发送前的判定与登记前的判重
        //   都被租户过滤器限定在当前租户。全局唯一会让租户 B 看不见租户 A 的那一行、判重读到
        //   「还没有」、插入撞约束 —— 收件人在 B 里怎么都退订不掉，而一键退订恰恰是法规要求必须生效的那条路。
        //   TenantId 为 NULL 的行（宿主上下文登记的）在 PostgreSQL / SQLite 上不被含 TenantId 的索引
        //   互相约束（NULL 互不相等，同上），所以按「有无租户」再各拆一对，宿主行照样一行一件事。
        if (multiTenancyEnabled)
        {
            var tenantSet = IndexFilterFactory.GetColumnNotNull("TenantId");
            var hostOnly = IndexFilterFactory.GetColumnNull("TenantId");
            var categorySet = IndexFilterFactory.GetColumnNotNull("Category");
            var allCategories = IndexFilterFactory.GetColumnNull("Category");

            builder.HasIndex(o => new { o.TenantId, o.Address, o.Channel, o.Category }).IsUnique()
                .HasFilter($"{tenantSet} AND {categorySet}")
                .HasDatabaseName("IX_Notification_OptOut_Tenant_Address_Channel_Category");
            builder.HasIndex(o => new { o.TenantId, o.Address, o.Channel }).IsUnique()
                .HasFilter($"{tenantSet} AND {allCategories}")
                .HasDatabaseName("IX_Notification_OptOut_Tenant_Address_Channel_AllCategories");
            builder.HasIndex(o => new { o.Address, o.Channel, o.Category }).IsUnique()
                .HasFilter($"{hostOnly} AND {categorySet}")
                .HasDatabaseName("IX_Notification_OptOut_Host_Address_Channel_Category");
            builder.HasIndex(o => new { o.Address, o.Channel }).IsUnique()
                .HasFilter($"{hostOnly} AND {allCategories}")
                .HasDatabaseName("IX_Notification_OptOut_Host_Address_Channel_AllCategories");
            return;
        }

        builder.HasIndex(o => new { o.Address, o.Channel, o.Category }).IsUnique()
            .HasFilter(IndexFilterFactory.GetColumnNotNull("Category"));
        builder.HasIndex(o => new { o.Address, o.Channel }).IsUnique()
            .HasFilter(IndexFilterFactory.GetColumnNull("Category"))
            .HasDatabaseName("IX_Notification_OptOut_Address_Channel_AllCategories");
    }
}
