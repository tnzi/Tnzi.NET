using Tnzi.Extensions;

namespace Tnzi.Identity.Organization.Tests;

using Tnzi.Identity.Organization.Entities;

/// <summary>
/// 拆分不改表名：<c>Identity_Organization</c> 在拆分前后逐字相同，因此这次拆分**不产生迁移**。
/// </summary>
/// <remarks>
/// <para>
/// 表名前缀是<b>按实体所在程序集</b>去模块容器里查的（<c>TableNamePrefixConfiguration</c>）：
/// 实体搬进 <c>Tnzi.Identity.Organization</c> 之后，回答前缀的就不再是 <c>IdentityModule</c>
/// 而是 <c>IdentityOrganizationModule</c>。少写那一行 <c>TableNamePrefix =&gt; "Identity"</c>，
/// 查不到前缀就<b>一声不吭地返回 null</b>，这张表安静地变成 <c>Organization</c> ——
/// 下一次生成迁移时是一条 rename，而在已有数据的库上那意味着一张空表加一张孤儿表。
/// </para>
/// <para>
/// 所以这条断言守的不是"某个字符串等于某个字符串"，而是<b>那一行还在</b>。
/// 走的是框架真的用的那段解析代码，不是复述一遍规则。
/// </para>
/// </remarks>
public class TableNamingTests
{
    /// <summary>只装两个 Identity 模块的最小模块容器（前缀解析只看程序集与模块实例）。</summary>
    private static IModuleContainer Container() => new ModuleContainer(
    [
        new ModuleDescriptor(typeof(IdentityModule), new IdentityModule()),
        new ModuleDescriptor(typeof(IdentityOrganizationModule), new IdentityOrganizationModule()),
    ]);

    /// <summary>把 <c>protected</c> 的前缀解析暴露出来 —— 走的是框架真的用的那段代码。</summary>
    private sealed class PrefixProbe(IModuleContainer container) : TableNamePrefixConfiguration(container)
    {
        public string? PrefixFor(Type entityType) => GetTableNamePrefix(entityType);
    }

    [Fact]
    public void MovedEntity_KeepsItsTableName()
    {
        var probe = new PrefixProbe(Container());

        var prefix = probe.PrefixFor(typeof(Organization));

        prefix.ShouldNotBeNullOrEmpty(
            "Organization 解析不出表名前缀 —— IdentityOrganizationModule.TableNamePrefix 丢了，这张表会掉掉 Identity_ 前缀");
        $"{prefix}_{nameof(Organization)}".ShouldBe("Identity_Organization");
    }

    /// <summary>
    /// 子模块声明的前缀必须与父模块**逐字相同** —— 这才是"零迁移"的充要条件。
    /// </summary>
    [Fact]
    public void TheChildDeclaresTheParentsPrefixVerbatim()
    {
        new IdentityOrganizationModule().TableNamePrefix.ShouldBe(new IdentityModule().TableNamePrefix);
        new IdentityOrganizationModule().TableNamePrefix.ShouldBe("Identity");
    }

    /// <summary>
    /// 同一个容器里，核心实体的前缀不受影响 —— 证明上面那条不是靠"整个容器只有一个前缀"蒙对的。
    /// </summary>
    [Fact]
    public void CoreEntitiesAreUnaffected()
    {
        var probe = new PrefixProbe(Container());

        probe.PrefixFor(typeof(User)).ShouldBe("Identity");
    }

    /// <summary>
    /// 只装父模块（= 消费方没加载本包）时，本包的实体解析不出前缀。
    /// </summary>
    /// <remarks>
    /// 这条是上面那条的对照组：它证明前缀确实是**本模块**回答的，而不是碰巧被父模块
    /// 认领了。两条一起才说得清"少写那一行会发生什么"。
    /// </remarks>
    [Fact]
    public void WithoutTheChildModule_ThePrefixCannotBeResolved()
    {
        var onlyParent = new ModuleContainer([new ModuleDescriptor(typeof(IdentityModule), new IdentityModule())]);
        var probe = new PrefixProbe(onlyParent);

        probe.PrefixFor(typeof(Organization)).ShouldBeNull();
    }
}
