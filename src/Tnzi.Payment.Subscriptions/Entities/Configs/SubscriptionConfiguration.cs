namespace Tnzi.Payment.Subscriptions.Entities;

public class SubscriptionConfiguration : EntityTypeConfigurationBase<Subscription, Guid>
{
    public override void Configure(EntityTypeBuilder<Subscription> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(s => s.SubscriptionNo).HasMaxLength(64).IsRequired();
        builder.Property(s => s.CancelReason).HasMaxLength(500);
        builder.Property(s => s.ChannelCode).HasMaxLength(32).IsRequired();
        builder.Property(s => s.Currency).HasMaxLength(8).IsRequired().HasDefaultValue("USD");
        builder.Property(s => s.DiscountAmount).HasMoneyPrecision();
        builder.Property(s => s.OriginalPrice).HasMoneyPrecision();
        builder.Property(s => s.PaidAmount).HasMoneyPrecision();
        builder.Property(s => s.ProviderCustomerId).HasMaxLength(128);
        builder.Property(s => s.PaymentMethodToken).HasMaxLength(128);
        builder.Property(s => s.PaymentMethodBrand).HasMaxLength(32);
        builder.Property(s => s.PaymentMethodLast4).HasMaxLength(8);
        builder.Property(s => s.LastBillingTradeNo).HasMaxLength(64);
        builder.Property(s => s.CustomerName).HasMaxLength(256);
        builder.Property(s => s.CustomerEmail).HasMaxLength(256);
        builder.Property(s => s.ProductCode).HasMaxLength(64);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(s => new { s.TenantId, s.SubscriptionNo }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(s => s.TenantId);
        }
        else
        {
            builder.HasIndex(s => s.SubscriptionNo).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.PlanId);
        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => s.EndTime);

        ConfigureActiveSubscriptionUniqueness(builder, multiTenancyEnabled);
        // 后台续费/过期扫描：按状态 + 下次计费时间过滤
        builder.HasIndex(s => new { s.Status, s.NextBillingTime });
        // 后台试用转正扫描：按状态 + 试用结束时间过滤
        builder.HasIndex(s => new { s.Status, s.TrialEndTime });
        // 后台暂停恢复扫描：按状态 + 恢复时间过滤
        builder.HasIndex(s => new { s.Status, s.PausedUntil });
    }

    /// <summary>
    /// 「同一用户在同一产品下至多一条有效订阅」——由数据库兜底。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 服务层那次「有没有有效订阅」的查询与写入之间隔着绑卡、试算券、计税、建首期支付单，
    /// 好几次外部调用。结账页一次双击落在这个窗口里，两条请求都读到「没有」，
    /// 于是两条 Active 订阅、两次首付、此后每个周期双倍扣款。索引不唯一时，
    /// 这条不变量没有任何东西在守 —— 那次查询只是个提示。
    /// </para>
    /// <para>
    /// ★ <b>拆成两条索引</b>，因为 <c>ProductCode</c> 可空而各家数据库对唯一索引里的 NULL
    /// 判定不同：PostgreSQL / SQLite 认为 NULL 互不相等（同一用户可以有任意多条无产品订阅），
    /// SQL Server 认为 NULL 彼此相等。写成一条时，这条约束在不同 provider 上表达的是两件事，
    /// 而「在另一个库上跑得好好的」正是这类缺陷最擅长的伪装。
    /// 这里 NULL 表示「这台宿主不区分产品」，是一个**共有的状态**，因此那一支同样只许一条。
    /// </para>
    /// <para>
    /// ★ 过滤条件排掉 <c>Cancelled</c> / <c>Expired</c>：已终止的订阅不占名额，
    /// 否则退订之后再也订不回同一个产品 —— 一条约束把正常业务动作永久堵死，比它防的问题更糟。
    /// </para>
    /// </remarks>
    private static void ConfigureActiveSubscriptionUniqueness(EntityTypeBuilder<Subscription> builder, bool multiTenancyEnabled)
    {
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        var productCode = IndexFilterFactory.QuoteIdentifier(nameof(Subscription.ProductCode), provider);
        var status = IndexFilterFactory.QuoteIdentifier(nameof(Subscription.Status), provider);

        var stillCounts =
            $"{status} <> {(int)SubscriptionStatus.Cancelled} "
            + $"AND {status} <> {(int)SubscriptionStatus.Expired} "
            + $"AND {IndexFilterFactory.GetIsDeletedFalse(provider)}";

        // ★ 两条索引都**显式具名**：不具名时 EF 按列组合去重，与已有的索引撞上就会安静地互相覆盖
        // （编译过、启动过、迁移里只剩一条）。NULL 那一支尤其不能带上 ProductCode 列 ——
        // PostgreSQL / SQLite 认为唯一索引里的 NULL 互不相等，带上它这条约束就等于不存在，
        // 而在 SQL Server 上它又是生效的：同一份代码在不同 provider 上守着不同的东西。
        // 只按用户建（外加 "ProductCode IS NULL" 的过滤条件）在四家上表达的是同一件事。
        if (multiTenancyEnabled)
        {
            builder.HasIndex(s => new { s.TenantId, s.UserId, s.ProductCode }, "IX_Subscription_ActiveProductPerUser")
                .IsUnique().HasFilter($"{productCode} IS NOT NULL AND {stillCounts}");
            builder.HasIndex(s => new { s.TenantId, s.UserId }, "IX_Subscription_ActiveWithoutProductPerUser")
                .IsUnique().HasFilter($"{productCode} IS NULL AND {stillCounts}");
        }
        else
        {
            builder.HasIndex(s => new { s.UserId, s.ProductCode }, "IX_Subscription_ActiveProductPerUser")
                .IsUnique().HasFilter($"{productCode} IS NOT NULL AND {stillCounts}");
            builder.HasIndex(s => s.UserId, "IX_Subscription_ActiveWithoutProductPerUser")
                .IsUnique().HasFilter($"{productCode} IS NULL AND {stillCounts}");
        }
    }
}
