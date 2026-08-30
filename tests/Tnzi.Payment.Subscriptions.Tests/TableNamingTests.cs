using Tnzi.Extensions;

namespace Tnzi.Payment.Subscriptions.Tests;

/// <summary>
/// 拆分不改表名：三张表在拆分前后都叫 <c>Payment_*</c>。
/// </summary>
/// <remarks>
/// <para>
/// 表名前缀是<b>按实体所在程序集</b>去模块容器里查的（<c>src/Tnzi.EFCore/.../TableNamePrefixConfiguration.cs</c>）：
/// 实体搬进 <c>Tnzi.Payment.Subscriptions</c> 之后，回答前缀的就不再是 <c>PaymentModule</c> 而是
/// <c>PaymentSubscriptionsModule</c>。少写那一行 <c>TableNamePrefix =&gt; "Payment"</c>，
/// 查不到前缀就<b>一声不吭地返回 null</b>，三张表安静地变成 <c>Subscription</c> /
/// <c>SubscriptionPlan</c> / <c>SubscriptionChange</c> —— 编译照过、全部业务测试照绿，
/// 损害要到下一次生成迁移时才显形：三条 rename，而在已有数据的库上那意味着三张空表加三张孤儿表。
/// </para>
/// <para>
/// 所以这条断言守的不是「某个字符串等于某个字符串」，而是<b>那一行还在</b>。
/// 走的也是框架真的用的那段解析代码，不是把规则在测试里重写一遍。
/// </para>
/// <para>
/// ★ 注意与本次拆分那一条<b>真的</b>迁移区分：表名没有任何变化，需要迁移的是
/// <c>Payment_Payment</c> 上那个由死导航生成的影子列 <c>SubscriptionId</c>（见模块注释）。
/// 两件事互不相干，别把「有一条迁移」读成「表名变了」。
/// </para>
/// </remarks>
public class TableNamingTests
{
    /// <summary>只装两个 Payment 模块的最小模块容器（前缀解析只看程序集与模块实例）。</summary>
    private static IModuleContainer Container() => new ModuleContainer(
    [
        new ModuleDescriptor(typeof(PaymentModule), new PaymentModule()),
        new ModuleDescriptor(typeof(PaymentSubscriptionsModule), new PaymentSubscriptionsModule()),
    ]);

    /// <summary>把 <c>protected</c> 的前缀解析暴露出来 —— 走的是框架真的用的那段代码。</summary>
    private sealed class PrefixProbe(IModuleContainer container) : TableNamePrefixConfiguration(container)
    {
        public string? PrefixFor(Type entityType) => GetTableNamePrefix(entityType);
    }

    [Theory]
    [InlineData(typeof(Subscription), "Payment_Subscription")]
    [InlineData(typeof(SubscriptionPlan), "Payment_SubscriptionPlan")]
    [InlineData(typeof(SubscriptionChange), "Payment_SubscriptionChange")]
    public void MovedEntities_KeepTheirTableNames(Type entityType, string expectedTableName)
    {
        var probe = new PrefixProbe(Container());

        var prefix = probe.PrefixFor(entityType);

        prefix.ShouldNotBeNullOrEmpty(
            $"{entityType.Name} 解析不出表名前缀 —— PaymentSubscriptionsModule.TableNamePrefix 丢了，这张表会掉掉 Payment_ 前缀");
        $"{prefix}_{entityType.Name}".ShouldBe(expectedTableName);
    }

    /// <summary>
    /// 子模块声明的前缀必须与父模块**逐字相同** —— 这才是「表名零变化」的充要条件。
    /// </summary>
    [Fact]
    public void TheChildDeclaresTheParentsPrefixVerbatim()
    {
        new PaymentSubscriptionsModule().TableNamePrefix.ShouldBe(new PaymentModule().TableNamePrefix);
        new PaymentSubscriptionsModule().TableNamePrefix.ShouldBe("Payment");
    }

    /// <summary>
    /// 同一个容器里，父模块实体的前缀不受影响 —— 证明上面那条不是靠「整个容器只有一个前缀」蒙对的。
    /// </summary>
    [Fact]
    public void ParentEntitiesAreUnaffected()
    {
        var probe = new PrefixProbe(Container());

        probe.PrefixFor(typeof(PaymentEntity)).ShouldBe("Payment");
        probe.PrefixFor(typeof(StoredPaymentMethod)).ShouldBe("Payment");
    }

    /// <summary>
    /// 本模块是 <c>TnziApplicationModule</c>：只有它才有 <c>TableNamePrefix</c> 这个扩展点。
    /// </summary>
    /// <remarks>
    /// 有表的子模块写成 <c>TnziCustomModule</c> 编译照过，但前缀那一行没地方放，
    /// 于是三张表静默掉前缀 —— 与「忘了写那一行」是同一种损害的另一个入口。
    /// </remarks>
    [Fact]
    public void TheChildIsAnApplicationModule()
    {
        new PaymentSubscriptionsModule().ShouldBeAssignableTo<TnziApplicationModule>();
    }
}
