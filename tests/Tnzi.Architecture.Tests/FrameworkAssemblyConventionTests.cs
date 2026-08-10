using System.Reflection;
using Tnzi.DependencyInjection;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// Architecture gate: framework assemblies (<c>Tnzi.*</c>) MUST register services manually
/// in <c>ConfigureServicesAsync</c> per CLAUDE.md rule #1. Marker interfaces
/// (<see cref="ISingletonDependency"/>, <see cref="IScopedDependency"/>,
/// <see cref="ITransientDependency"/>) are reserved for application-layer auto-registration
/// and may NOT appear on types in framework assemblies - they are silently skipped at
/// runtime by <c>DependencyRegistrationService</c>, leading to "type works in tests but
/// fails at runtime" surprises.
/// </summary>
/// <remarks>
/// 搬到本项目的理由：它扫的是<b>已加载</b>的程序集，而程序集加载此前靠的是
/// <c>HostingModule</c> 上 <c>[OptionalDependsOn(typeof(X))]</c> 里 <c>typeof</c> 求值的副作用。
/// 不在那份属性列表里的模块（Redis / RabbitMQ / Kafka / OpenTelemetry / Imaging …）
/// 因此从未被它扫描过。本项目直接引用全部模块并经
/// <see cref="AllModulesStartupModule"/> 加载，覆盖面才与「框架程序集」这个说法相符。
/// </remarks>
public class FrameworkAssemblyConventionTests
{
    /// <summary>
    /// Marker interfaces forbidden on framework-assembly types.
    /// </summary>
    private static readonly Type[] ForbiddenMarkers =
    [
        typeof(ISingletonDependency),
        typeof(IScopedDependency),
        typeof(ITransientDependency)
    ];

    [Fact]
    public void FrameworkAssemblies_MustNot_UseAutoRegistrationMarkers()
    {
        // Load the full module graph so every Tnzi.* assembly used by the framework gets pulled in.
        ArchitectureModuleGraph.Load();

        var violations = AppDomain.CurrentDomain.GetAssemblies()
            .Where(IsFrameworkAssembly)
            .SelectMany(GetTypesSafe)
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .SelectMany(t => ForbiddenMarkers
                .Where(marker => marker.IsAssignableFrom(t))
                .Select(marker => (Type: t, Marker: marker)))
            .ToList();

        if (violations.Count > 0)
        {
            var report = string.Join(Environment.NewLine,
                violations.Select(v =>
                    $"  - {v.Type.FullName} in {v.Type.Assembly.GetName().Name} implements {v.Marker.Name} (must register manually in module's ConfigureServicesAsync)"));
            Assert.Fail(
                $"Found {violations.Count} framework-assembly type(s) using forbidden auto-registration markers:{Environment.NewLine}{report}");
        }
    }

    private static bool IsFrameworkAssembly(Assembly a)
    {
        var name = a.GetName().Name;
        return name is not null
            && name.StartsWith("Tnzi.", StringComparison.OrdinalIgnoreCase)
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
            // A loader failure usually means a missing optional dependency; the partial type list
            // still tells us about marker-interface usage on the types that DID load. Surface the
            // failure to the test output so a real misconfiguration isn't silently masked.
            Console.Error.WriteLine(
                $"[FrameworkAssemblyConventionTests] {a.GetName().Name}: ReflectionTypeLoadException - {ex.Message}");
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
