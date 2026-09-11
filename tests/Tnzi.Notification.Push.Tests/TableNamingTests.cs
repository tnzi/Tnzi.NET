using Tnzi.EFCore;
using Tnzi.Extensions;
using Tnzi.Modules;
using Tnzi.Notification.Push.Entities;

namespace Tnzi.Notification.Push.Tests;

/// <summary>
/// 设备注册表必须落在 <c>Notification_PushDevice</c> —— 与父模块共享前缀。
/// </summary>
/// <remarks>
/// <para>
/// 表名前缀是<b>按实体所在程序集</b>去模块容器里查的
/// （<c>src/Tnzi.EFCore/TableNamePrefixConfiguration.cs</c>）：实体住在
/// <c>Tnzi.Notification.Push</c>，回答前缀的就是 <c>NotificationPushModule</c> 而不是
/// <c>NotificationModule</c>。少写那一行 <c>TableNamePrefix =&gt; "Notification"</c>，
/// 查不到前缀会<b>一声不吭地返回 null</b>，表安静地变成 <c>PushDevice</c> ——
/// 编译照过、全部业务测试照绿（SQLite 按模型说什么就建什么），损害要到消费方生成迁移时
/// 才显形，而那时它已经和 Notification 的其它表分了家。
/// </para>
/// <para>
/// ★ <b>本项目的其它测试都验不到这件事</b>：<c>PushDeviceIntegrationTests</c> 的
/// <c>PushTestDbContext</c> 直接 <c>ApplyConfiguration</c>，整条前缀链路根本没跑。
/// 所以这条断言不是重复覆盖，它是唯一的覆盖。
/// </para>
/// </remarks>
public class TableNamingTests
{
    /// <summary>只装两个 Notification 模块的最小模块容器（前缀解析只看程序集与模块实例）。</summary>
    private static IModuleContainer Container() => new ModuleContainer(
    [
        new ModuleDescriptor(typeof(NotificationModule), new NotificationModule()),
        new ModuleDescriptor(typeof(NotificationPushModule), new NotificationPushModule()),
    ]);

    /// <summary>把 <c>protected</c> 的前缀解析暴露出来 —— 走的是框架真的用的那段代码。</summary>
    private sealed class PrefixProbe(IModuleContainer container) : TableNamePrefixConfiguration(container)
    {
        public string? PrefixFor(Type entityType) => GetTableNamePrefix(entityType);
    }

    /// <summary>设备表带 <c>Notification_</c> 前缀。</summary>
    [Fact]
    public void PushDevice_LandsUnderTheNotificationPrefix()
    {
        var probe = new PrefixProbe(Container());

        var prefix = probe.PrefixFor(typeof(PushDevice));

        prefix.ShouldNotBeNullOrEmpty(
            "PushDevice 解析不出表名前缀 —— NotificationPushModule.TableNamePrefix 丢了，这张表会掉掉 Notification_ 前缀");
        $"{prefix}_{nameof(PushDevice)}".ShouldBe("Notification_PushDevice");
    }

    /// <summary>
    /// 子模块声明的前缀必须与父模块<b>逐字相同</b>：拆的是程序集不是 schema。
    /// </summary>
    [Fact]
    public void TheChildDeclaresTheParentsPrefixVerbatim()
    {
        new NotificationPushModule().TableNamePrefix.ShouldBe(new NotificationModule().TableNamePrefix);
        new NotificationPushModule().TableNamePrefix.ShouldBe("Notification");
    }

    /// <summary>
    /// 同一个容器里，父模块实体的前缀不受影响 —— 证明上面那条不是靠「容器里只有一个前缀」蒙对的。
    /// </summary>
    [Fact]
    public void ParentEntitiesAreUnaffected()
    {
        var probe = new PrefixProbe(Container());

        probe.PrefixFor(typeof(Notification.Entities.Recipient)).ShouldBe("Notification");
    }

    /// <summary>
    /// 本模块是 <c>TnziApplicationModule</c>：只有它才有 <c>TableNamePrefix</c> 这个扩展点。
    /// </summary>
    /// <remarks>
    /// 带表的子模块写成 <c>TnziCustomModule</c> 编译照过，但前缀那一行没地方放，
    /// 于是表静默掉前缀 —— 与「忘了写那一行」是同一种损害的另一个入口。
    /// 本模块 2026-09-02 之前正是 <c>TnziCustomModule</c>（那时它确实无表）。
    /// </remarks>
    [Fact]
    public void TheChildIsAnApplicationModule()
    {
        new NotificationPushModule().ShouldBeAssignableTo<TnziApplicationModule>();
    }
}
