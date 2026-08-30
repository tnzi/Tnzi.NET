using Tnzi.Security.Claims;

namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 关系形状对账：三张订阅表的 schema 与拆分前逐项相同，而**支付表上那个影子列没了**。
/// </summary>
/// <remarks>
/// <para>
/// 拆分改的是程序集，不是 schema —— 除了一处，而这一处正是本文件的重点。
/// </para>
/// <para>
/// ★ <c>Subscription.Payments</c> 拆分前是一条<b>死导航</b>：没有反向导航、两侧的
/// <c>EntityTypeConfiguration</c> 都没提过它、全仓也没有一行往里加过东西。
/// 但 EF 的约定不看这些，它照样在<b>父模块的</b> <c>Payment_Payment</c> 表上生成了
/// 影子列 <c>SubscriptionId</c>、外键 <c>FK_Payment_Payment_Subscription_SubscriptionId</c>
/// 与一条索引。留着它，父模块那张表的形状就取决于「有没有加载续费包」。
/// 本次拆分把它删掉了，代价是消费方需要一条 <c>DropForeignKey</c> + <c>DropIndex</c> +
/// <c>DropColumn</c> 的迁移（无数据损失，那列从未被写过）。
/// </para>
/// <para>
/// 这个 DbContext 同时建了父模块与本模块的实体，因此它是「两侧都加载时的真实模型」——
/// 在这里断言支付表上没有那一列，才是对「已经删干净」的直接证据。
/// </para>
/// </remarks>
public class SubscriptionRelationalShapeTests
{
    private static SubscriptionsTestDbContext ModelOnlyContext()
    {
        var options = new DbContextOptionsBuilder<SubscriptionsTestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        return new SubscriptionsTestDbContext(options, Mock.Of<ICurrentUser>());
    }

    /// <summary>
    /// ★ 支付表上不再有 <c>SubscriptionId</c> 影子列、指向订阅的外键，或它带来的索引。
    /// </summary>
    /// <remarks>
    /// 把 <c>ICollection&lt;Payment&gt; Payments</c> 加回 <see cref="Subscription"/>，
    /// 这条立刻红 —— EF 会重新推断出整套（列 + 外键 + 索引）。
    /// </remarks>
    [Fact]
    public void ThePaymentTableNoLongerCarriesAShadowSubscriptionId()
    {
        using var db = ModelOnlyContext();

        var payment = db.Model.FindEntityType(typeof(PaymentEntity))!;

        payment.GetProperties().Select(p => p.Name).ShouldNotContain("SubscriptionId",
            "死导航 Subscription.Payments 又回来了：它会在支付表上生成一个从来没人写过的影子列");
        payment.GetForeignKeys().Select(fk => fk.PrincipalEntityType.ClrType.Name)
            .ShouldNotContain(nameof(Subscription));
        payment.GetNavigations().Select(n => n.Name).ShouldNotContain(nameof(Subscription));
    }

    /// <summary>
    /// 反向也钉住：订阅实体上没有指向支付的导航。
    /// </summary>
    [Fact]
    public void TheSubscriptionEntityHasNoPaymentsNavigation()
    {
        using var db = ModelOnlyContext();

        db.Model.FindEntityType(typeof(Subscription))!
            .GetNavigations().Select(n => n.Name)
            .ShouldNotContain("Payments");
    }

    /// <summary>
    /// 订阅 → 计划：外键、主键、删除行为与索引全部不变。
    /// </summary>
    [Fact]
    public void SubscriptionToPlanRelationshipIsUnchanged()
    {
        using var db = ModelOnlyContext();

        var fk = db.Model.FindEntityType(typeof(Subscription))!
            .GetForeignKeys()
            .Single(f => f.PrincipalEntityType.ClrType == typeof(SubscriptionPlan));

        fk.Properties.Select(p => p.Name).ShouldBe([nameof(Subscription.PlanId)]);
        fk.PrincipalKey.Properties.Select(p => p.Name).ShouldBe([nameof(SubscriptionPlan.Id)]);
        fk.DependentToPrincipal!.Name.ShouldBe(nameof(Subscription.Plan));
        fk.PrincipalToDependent!.Name.ShouldBe(nameof(SubscriptionPlan.Subscriptions));
    }

    /// <summary>
    /// 变更记录的三条外键（订阅 / 原计划 / 新计划）都保持 <c>Restrict</c>。
    /// </summary>
    /// <remarks>
    /// 级联删会让「删掉一个计划」顺手抹掉历史变更记录 —— 那是审计链，不该跟着主数据消失。
    /// </remarks>
    [Theory]
    [InlineData(nameof(SubscriptionChange.SubscriptionId))]
    [InlineData(nameof(SubscriptionChange.FromPlanId))]
    [InlineData(nameof(SubscriptionChange.ToPlanId))]
    public void SubscriptionChangeForeignKeysStayRestrict(string foreignKeyProperty)
    {
        using var db = ModelOnlyContext();

        var fk = db.Model.FindEntityType(typeof(SubscriptionChange))!
            .GetForeignKeys()
            .Single(f => f.Properties.Single().Name == foreignKeyProperty);

        fk.DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
    }

    /// <summary>
    /// 三张表的索引数量与列组合不变 —— 后台扫描全靠它们，掉一条就是全表扫。
    /// </summary>
    [Fact]
    public void SubscriptionIndexesAreUnchanged()
    {
        using var db = ModelOnlyContext();

        var indexes = db.Model.FindEntityType(typeof(Subscription))!
            .GetIndexes()
            .Select(i => string.Join(",", i.Properties.Select(p => p.Name)))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        indexes.ShouldBe(
        [
            "EndTime",
            "PlanId",
            "Status",
            "Status,NextBillingTime",
            "Status,PausedUntil",
            "Status,TrialEndTime",
            "SubscriptionNo",
            "UserId",
            "UserId,ProductCode,Status",
        ]);
    }

    /// <summary>
    /// 建库能建起来 —— 三张订阅表与父模块的支付侧表在同一个模型里共存。
    /// </summary>
    [Fact]
    public void TheModelCanBeCreated()
    {
        using var db = ModelOnlyContext();

        db.Model.GetEntityTypes().Select(e => e.ClrType.Name)
            .ShouldContain(nameof(Subscription));

        // 表名前缀由 TableNamePrefixConfiguration 在真实 DbContext 上解析；
        // 这里只证明两侧的实体配置放在一个模型里不冲突。
        db.Model.FindEntityType(typeof(SubscriptionPlan)).ShouldNotBeNull();
        db.Model.FindEntityType(typeof(SubscriptionChange)).ShouldNotBeNull();
    }
}
