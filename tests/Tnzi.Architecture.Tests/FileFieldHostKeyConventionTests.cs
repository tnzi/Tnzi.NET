using System.Reflection;
using Tnzi.Domain.Entities;
using Tnzi.Storage;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 框架自带的实体不得让 <see cref="StorageModule"/> 拒绝启动：<c>[FileField]</c> 只能长在主键是 Guid 的实体上。
/// </summary>
/// <remarks>
/// <para>
/// 存储模块在应用初始化时扫描已注册的实体，<c>[FileField]</c> 长在 long / int / string 主键上就拒绝启动
/// （引用表的 EntityId 是 Guid 列，此前那种声明被静默跳过，引用的文件随后被当成孤儿删掉）。
/// 那道检查只在真实启动时运行，而本项目的模块图门禁不走初始化阶段：一个框架模块新增这样的实体，
/// 所有加载存储模块的应用都会启动失败，而这里的每一道门禁照样是绿的。
/// </para>
/// <para>
/// 判定直接调用存储模块里的同一份实现（反射取内部类型），不在测试里另写一份会漂开的规则。
/// </para>
/// </remarks>
public class FileFieldHostKeyConventionTests
{
    [Fact]
    public void FrameworkEntitiesWithFileFields_HaveGuidKeys()
    {
        var unsupported = FindUnsupportedHosts(FrameworkEntityTypes());

        Assert.True(unsupported.Count == 0,
            "These framework entities declare [FileField] on a non-Guid key; every app that loads the storage module would refuse to start: "
            + string.Join(", ", unsupported));
    }

    /// <summary>反向守卫：扫描面里确实有带 <c>[FileField]</c> 的框架实体，否则上面那条对空集恒绿。</summary>
    [Fact]
    public void Scan_CoversTheKnownFileFieldEntities()
    {
        var withFileFields = FrameworkEntityTypes()
            .Where(t => t.GetProperties().Any(p => p.GetCustomAttribute<FileFieldAttribute>() != null))
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        new[] { "UserDetail", "ChatMessage", "Envelope", "Receipt" }.ShouldBeSubsetOf(withFileFields);
    }

    private static IReadOnlyList<string> FindUnsupportedHosts(IEnumerable<Type> entityTypes)
    {
        var guard = typeof(StorageModule).Assembly.GetType("Tnzi.Storage.Services.FileFieldHostKeyGuard", throwOnError: true)!;
        var find = guard.GetMethod("FindUnsupportedHosts", BindingFlags.Public | BindingFlags.Static)!;
        return (IReadOnlyList<string>)find.Invoke(null, [entityTypes])!;
    }

    private static List<Type> FrameworkEntityTypes()
    {
        ArchitectureModuleGraph.Load();

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(IsFrameworkAssembly)
            .SelectMany(GetTypesSafe)
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition && typeof(IEntity).IsAssignableFrom(t))
            .ToList();
    }

    private static bool IsFrameworkAssembly(Assembly a)
    {
        var name = a.GetName().Name;
        return name is not null
            && name.StartsWith("Tnzi", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".TestBase", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<Type> GetTypesSafe(Assembly a)
    {
        try
        {
            return a.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
