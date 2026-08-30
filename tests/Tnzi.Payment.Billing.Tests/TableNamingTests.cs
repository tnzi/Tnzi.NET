using Tnzi.Extensions;

namespace Tnzi.Payment.Billing.Tests;

/// <summary>
/// 拆分不改表名：两张表在拆分前后都叫 <c>Payment_*</c>，因此这次拆分**不产生任何迁移**。
/// </summary>
/// <remarks>
/// <para>
/// 表名前缀是<b>按实体所在程序集</b>去模块容器里查的（<c>src/Tnzi.EFCore/.../TableNamePrefixConfiguration.cs</c>）：
/// 实体搬进 <c>Tnzi.Payment.Billing</c> 之后，回答前缀的就不再是 <c>PaymentModule</c> 而是
/// <c>PaymentBillingModule</c>。少写那一行 <c>TableNamePrefix =&gt; "Payment"</c>，
/// 查不到前缀就<b>一声不吭地返回 null</b>，两张表安静地变成 <c>Invoice</c> / <c>InvoiceLineItem</c> ——
/// 编译照过、全部业务测试照绿，损害要到下一次生成迁移时才显形：两条 rename，
/// 而在已有数据的库上那意味着两张空表加两张孤儿表。
/// </para>
/// <para>
/// 所以这条断言守的不是「某个字符串等于某个字符串」，而是<b>那一行还在</b>。
/// 走的也是框架真的用的那段解析代码，不是把规则在测试里重写一遍。
/// </para>
/// </remarks>
public class TableNamingTests
{
    /// <summary>只装两个 Payment 模块的最小模块容器（前缀解析只看程序集与模块实例）。</summary>
    private static IModuleContainer Container() => new ModuleContainer(
    [
        new ModuleDescriptor(typeof(PaymentModule), new PaymentModule()),
        new ModuleDescriptor(typeof(PaymentBillingModule), new PaymentBillingModule()),
    ]);

    /// <summary>把 <c>protected</c> 的前缀解析暴露出来 —— 走的是框架真的用的那段代码。</summary>
    private sealed class PrefixProbe(IModuleContainer container) : TableNamePrefixConfiguration(container)
    {
        public string? PrefixFor(Type entityType) => GetTableNamePrefix(entityType);
    }

    [Theory]
    [InlineData(typeof(Invoice), "Payment_Invoice")]
    [InlineData(typeof(InvoiceLineItem), "Payment_InvoiceLineItem")]
    public void MovedEntities_KeepTheirTableNames(Type entityType, string expectedTableName)
    {
        var probe = new PrefixProbe(Container());

        var prefix = probe.PrefixFor(entityType);

        prefix.ShouldNotBeNullOrEmpty(
            $"{entityType.Name} 解析不出表名前缀 —— PaymentBillingModule.TableNamePrefix 丢了，这张表会掉掉 Payment_ 前缀");
        $"{prefix}_{entityType.Name}".ShouldBe(expectedTableName);
    }

    /// <summary>
    /// 子模块声明的前缀必须与父模块**逐字相同** —— 这才是「零迁移」的充要条件。
    /// </summary>
    [Fact]
    public void TheChildDeclaresTheParentsPrefixVerbatim()
    {
        new PaymentBillingModule().TableNamePrefix.ShouldBe(new PaymentModule().TableNamePrefix);
        new PaymentBillingModule().TableNamePrefix.ShouldBe("Payment");
    }

    /// <summary>
    /// 同一个容器里，父模块实体的前缀不受影响 —— 证明上面那条不是靠「整个容器只有一个前缀」蒙对的。
    /// </summary>
    [Fact]
    public void ParentEntitiesAreUnaffected()
    {
        var probe = new PrefixProbe(Container());

        probe.PrefixFor(typeof(PaymentEntity)).ShouldBe("Payment");
    }

    /// <summary>
    /// 本模块是 <c>TnziApplicationModule</c>：只有它才有 <c>TableNamePrefix</c> 这个扩展点。
    /// </summary>
    /// <remarks>
    /// 有表的子模块写成 <c>TnziCustomModule</c> 编译照过，但前缀那一行没地方放，
    /// 于是两张表静默掉前缀 —— 与「忘了写那一行」是同一种损害的另一个入口。
    /// </remarks>
    [Fact]
    public void TheChildIsAnApplicationModule()
    {
        new PaymentBillingModule().ShouldBeAssignableTo<TnziApplicationModule>();
    }
}
