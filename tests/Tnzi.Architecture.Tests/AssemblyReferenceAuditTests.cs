using System.Reflection;
using Tnzi.AspNetCore;
using Tnzi.Hosting;
using Tnzi.Mapster;
using Tnzi.Modules;
using Tnzi.Modules.Diagnostics;
using Tnzi.Storage;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// <see cref="ModuleDependencyAuditor.AuditAssemblyReferences"/> 的行为。
/// </summary>
/// <remarks>
/// ★ <b>这里刻意不断言「框架自身零违规」。</b>实测不带参数跑一次得 41 条而真阳性为零
/// （40 条是宿主必载的 <c>AspNetCoreModule</c> / <c>MapsterModule</c>，1 条是只用了静态助手的
/// <c>Storage → Imaging</c>）。把它当框架门禁会立刻退化成一张全是豁免的名单 ——
/// 它的用武之地是<b>消费应用</b>按自己的模块收窄之后。
///
/// 所以这几条钉的是「两个参数真的起作用」与「可选依赖算已声明」，
/// 那才是消费方照着用时依赖的性质。
/// </remarks>
public class AssemblyReferenceAuditTests
{
    [Fact]
    public void AssumedLoaded_RemovesTheModulesTheHostGuarantees()
    {
        var result = ArchitectureModuleGraph.Load();

        var withoutHint = ModuleDependencyAuditor.AuditAssemblyReferences(result.Modules);
        var withHint = ModuleDependencyAuditor.AuditAssemblyReferences(
            result.Modules,
            assumedLoaded: [typeof(AspNetCoreModule), typeof(MapsterModule)]);

        // 存在性：不交代宿主必载模块时确实会报出它们，否则下面那条断言证明不了任何事。
        Assert.Contains(withoutHint, v => v.ProviderModule == typeof(AspNetCoreModule));
        Assert.DoesNotContain(withHint, v => v.ProviderModule == typeof(AspNetCoreModule));
        Assert.DoesNotContain(withHint, v => v.ProviderModule == typeof(MapsterModule));
        Assert.True(withHint.Count < withoutHint.Count);
    }

    [Fact]
    public void ModuleFilter_RestrictsWhichModulesAreAudited()
    {
        var result = ArchitectureModuleGraph.Load();

        var all = ModuleDependencyAuditor.AuditAssemblyReferences(result.Modules);
        var storageOnly = ModuleDependencyAuditor.AuditAssemblyReferences(
            result.Modules,
            moduleFilter: t => t == typeof(StorageModule));

        Assert.NotEmpty(storageOnly);
        Assert.All(storageOnly, v => Assert.Equal(typeof(StorageModule), v.ConsumerModule));
        Assert.True(storageOnly.Count < all.Count);
    }

    /// <summary>
    /// <c>[OptionalDependsOn]</c> 的语义是「我容忍它缺席」，报它就是在惩罚一个正确的设计。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="HostingModule"/> 做样本：它引用了三十来个业务模块的程序集，
    /// 而且<b>全部</b>是可选声明 —— 正是这个性质要被守住的地方。
    /// </remarks>
    [Fact]
    public void OptionalDependsOn_CountsAsDeclared()
    {
        var result = ArchitectureModuleGraph.Load();

        var hostingViolations = ModuleDependencyAuditor.AuditAssemblyReferences(
            result.Modules,
            moduleFilter: t => typeof(HostingModule).IsAssignableFrom(t));

        var optionallyDeclared = typeof(HostingModule)
            .GetCustomAttributes<OptionalDependsOnAttribute>(inherit: true)
            .SelectMany(a => a.DependedModuleTypes)
            .ToHashSet();

        Assert.NotEmpty(optionallyDeclared);
        Assert.All(hostingViolations, v => Assert.DoesNotContain(v.ProviderModule, optionallyDeclared));
    }

    [Fact]
    public void EmptyGraph_IsHandled()
    {
        Assert.Empty(ModuleDependencyAuditor.AuditAssemblyReferences([]));
    }
}
