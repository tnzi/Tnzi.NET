using System.Reflection;
using Tnzi.Mapping;

namespace Tnzi.Mapster.Tests;

/// <summary>
/// 扫描映射配置时，程序集里个别类型加载失败（引用了缺席的可选依赖）不能让同一程序集里的映射配置一起消失。
/// </summary>
public class MappingConfigScanTests
{
    private sealed class OrderMappingConfig : IMappingConfig
    {
        public void Configure(IMappingConfigContext context)
        {
        }
    }

    /// <summary>一个 <c>GetTypes()</c> 抛出部分加载异常的程序集：能加载的类型照常在 <c>Types</c> 里，加载不了的是 null。</summary>
    private sealed class PartiallyLoadableAssembly : Assembly
    {
        public override Type[] GetTypes() =>
            throw new ReflectionTypeLoadException(
                [typeof(OrderMappingConfig), null],
                [new FileNotFoundException("Could not load file or assembly 'Optional.Dependency'.")]);

        public override AssemblyName GetName() => new("Consumer.Module");
    }

    /// <summary>
    /// ★ 回归：此前整个程序集按「没有类型」处理且不留记录，同一程序集里的映射配置全部跳过，
    /// 改名属性与计算属性静默映射成默认值。
    /// </summary>
    [Fact]
    public void PartiallyLoadableAssembly_KeepsItsLoadableTypes_AndRecordsTheFailure()
    {
        var failures = new List<(string Assembly, string Error)>();

        var types = MapsterModule.GetLoadableTypes(new PartiallyLoadableAssembly(), failures);

        Assert.Equal([typeof(OrderMappingConfig)], types);
        var failure = Assert.Single(failures);
        Assert.Equal("Consumer.Module", failure.Assembly);
        Assert.Contains("Optional.Dependency", failure.Error);
    }

    [Fact]
    public void LoadableAssembly_RecordsNothing()
    {
        var failures = new List<(string Assembly, string Error)>();

        var types = MapsterModule.GetLoadableTypes(typeof(MappingConfigScanTests).Assembly, failures);

        Assert.Contains(typeof(OrderMappingConfig), types);
        Assert.Empty(failures);
    }
}
