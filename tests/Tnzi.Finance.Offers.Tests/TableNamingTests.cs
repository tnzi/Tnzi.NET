using Tnzi.Extensions;
using Tnzi.Modules;

namespace Tnzi.Finance.Offers.Tests;

/// <summary>
/// 拆分不改表名：四张表在拆分前后都叫 <c>Finance_*</c>，因此这次拆分**不产生任何迁移**。
/// </summary>
/// <remarks>
/// <para>
/// 表名前缀是<b>按实体所在程序集</b>去模块容器里查的（<c>TableNamePrefixConfiguration</c>）：
/// 实体搬进 <c>Tnzi.Finance.Offers</c> 之后，回答前缀的就不再是 <c>FinanceModule</c> 而是
/// <c>FinanceOffersModule</c>。少写那一行 <c>TableNamePrefix =&gt; "Finance"</c>，
/// 查不到前缀就<b>一声不吭地返回 null</b>，四张表安静地变成 <c>Estimate</c> / <c>EstimateLine</c> /
/// <c>PurchaseOrder</c> / <c>PurchaseOrderLine</c> —— 下一次生成迁移时是四条 rename，
/// 而在已有数据的库上那意味着四张空表加四张孤儿表。
/// </para>
/// <para>
/// 所以这条断言守的不是"某个字符串等于某个字符串"，而是<b>那一行还在</b>。
/// </para>
/// </remarks>
public class TableNamingTests
{
    /// <summary>只装两个 Finance 模块的最小模块容器（前缀解析只看程序集与模块实例）。</summary>
    private static IModuleContainer Container() => new ModuleContainer(
    [
        new ModuleDescriptor(typeof(FinanceModule), new FinanceModule()),
        new ModuleDescriptor(typeof(FinanceOffersModule), new FinanceOffersModule()),
    ]);

    /// <summary>把 <c>protected</c> 的前缀解析暴露出来 —— 走的是框架真的用的那段代码。</summary>
    private sealed class PrefixProbe(IModuleContainer container) : TableNamePrefixConfiguration(container)
    {
        public string? PrefixFor(Type entityType) => GetTableNamePrefix(entityType);
    }

    [Theory]
    [InlineData(typeof(Estimate), "Finance_Estimate")]
    [InlineData(typeof(EstimateLine), "Finance_EstimateLine")]
    [InlineData(typeof(PurchaseOrder), "Finance_PurchaseOrder")]
    [InlineData(typeof(PurchaseOrderLine), "Finance_PurchaseOrderLine")]
    public void MovedEntities_KeepTheirTableNames(Type entityType, string expectedTableName)
    {
        var probe = new PrefixProbe(Container());

        var prefix = probe.PrefixFor(entityType);

        prefix.ShouldNotBeNullOrEmpty(
            $"{entityType.Name} 解析不出表名前缀 —— FinanceOffersModule.TableNamePrefix 丢了，这张表会掉掉 Finance_ 前缀");
        $"{prefix}_{entityType.Name}".ShouldBe(expectedTableName);
    }

    /// <summary>
    /// 子模块声明的前缀必须与父模块**逐字相同** —— 这才是"零迁移"的充要条件。
    /// </summary>
    [Fact]
    public void TheChildDeclaresTheParentsPrefixVerbatim()
    {
        new FinanceOffersModule().TableNamePrefix.ShouldBe(new FinanceModule().TableNamePrefix);
        new FinanceOffersModule().TableNamePrefix.ShouldBe("Finance");
    }

    /// <summary>
    /// 同一个容器里，核心实体的前缀不受影响 —— 证明上面那条不是靠"整个容器只有一个前缀"蒙对的。
    /// </summary>
    [Fact]
    public void CoreEntitiesAreUnaffected()
    {
        var probe = new PrefixProbe(Container());

        probe.PrefixFor(typeof(Invoice)).ShouldBe("Finance");
    }
}
