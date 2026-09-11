using System.Reflection;
using Tnzi.Settings;

namespace Tnzi.Finance.Tests;

/// <summary>
/// <see cref="FinanceAccountingOptions"/> 是 <see cref="FinanceOptions"/> 几个会计字段的
/// **配置中心分组镜像**：同一个 <c>Finance</c> section、同名属性、同类型、**同默认值**。
/// </summary>
/// <remarks>
/// 镜像类的注释把「默认值必须一致」写成了铁律，但此前没有任何东西守它。漂移是静默的：
/// 镜像不注入任何服务、不参与运行时，改错一个默认值编译照过、测试照绿，只有配置中心的
/// 「恢复默认」会把一个运行时从未使用过的值写进去——而那正是操作员最信任的那个按钮。
/// <br/><br/>
/// 逐个属性反射而不是把四个字段抄成四条断言：抄的那一份自己也会漂移，
/// 而且新增一个镜像字段时没有任何东西提醒补断言。
/// </remarks>
public class FinanceAccountingOptionsMirrorTests
{
    private static readonly FinanceOptions Source = new();
    private static readonly FinanceAccountingOptions Mirror = new();

    private static IReadOnlyList<PropertyInfo> MirrorProperties =>
        [.. typeof(FinanceAccountingOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance)];

    [Fact]
    public void EveryMirroredProperty_HasTheSameNameTypeAndDefaultAsTheRuntimeSource()
    {
        // 空镜像会让下面的循环一条都不跑，断言变成永真——先钉住它确实有内容
        MirrorProperties.ShouldNotBeEmpty();

        foreach (var mirrored in MirrorProperties)
        {
            var source = typeof(FinanceOptions).GetProperty(mirrored.Name, BindingFlags.Public | BindingFlags.Instance);

            source.ShouldNotBeNull($"FinanceOptions has no '{mirrored.Name}' — the mirror must not invent settings the runtime never reads.");
            source.PropertyType.ShouldBe(mirrored.PropertyType, $"'{mirrored.Name}' differs in type between the runtime source and its mirror.");
            mirrored.GetValue(Mirror).ShouldBe(source.GetValue(Source),
                $"'{mirrored.Name}' default differs between FinanceOptions and its mirror — the config centre's \"restore default\" would write a value the runtime never had.");
        }
    }

    [Fact]
    public void BothTypes_BindToTheSameConfigurationSection()
    {
        // 同名属性同默认值只有在两者读同一个 section 时才有意义
        var source = typeof(FinanceOptions).GetCustomAttribute<ConfigSectionAttribute>();
        var mirror = typeof(FinanceAccountingOptions).GetCustomAttribute<ConfigSectionAttribute>();

        source.ShouldNotBeNull();
        mirror.ShouldNotBeNull();
        mirror.Section.ShouldBe(source.Section);
    }
}
