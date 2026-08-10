namespace Tnzi.Tests.Modules;

/// <summary>
/// <see cref="ModuleDependencyAuditor"/> 的判定规则。
/// </summary>
/// <remarks>
/// 这些用例存在的理由：审计器的三条放行规则（多注册者任一命中 / 可选构造参数 / 核心程序集模块）
/// 都是从**误报**里反推出来的，改回去不会让任何模块级测试变红，只有全模块图门禁会红 ——
/// 而那道门禁的结果会随模块增删漂移，不适合当这三条规则的守卫。
/// </remarks>
public class ModuleDependencyAuditTests
{
    // ── 测试替身 ───────────────────────────────────────────────────────────

    private interface IServiceFromA;
    private interface IServiceFromB;

    private sealed class ConsumerNeedingB
    {
        public ConsumerNeedingB(IServiceFromB _) { }
    }

    private sealed class ConsumerWithOptionalB
    {
        public ConsumerWithOptionalB(IServiceFromB? _ = null) { }
    }

    private sealed class ModuleA : TnziCustomModule;

    private sealed class ModuleB : TnziCustomModule;

    private sealed class FakeDescriptor(Type type, params IModuleDescriptor[] dependencies) : IModuleDescriptor
    {
        private readonly List<IModuleDescriptor> _dependencies = [.. dependencies];

        public Type Type { get; } = type;
        public ITnziModule Instance { get; } = (ITnziModule)Activator.CreateInstance(type)!;
        public Assembly Assembly { get; } = type.Assembly;
        public IReadOnlyList<IModuleDescriptor> Dependencies => _dependencies;
        public bool IsEnabled => true;
        public ModuleInitializationState InitializationState => ModuleInitializationState.Succeeded;
        public Exception? InitializationError => null;
        public ModuleManifest Manifest => ModuleManifest.Empty;

        public void AddDependency(IModuleDescriptor descriptor) => _dependencies.Add(descriptor);
    }

    private static ServiceDescriptor Registers<TService, TImpl>() where TImpl : class, TService where TService : class
        => ServiceDescriptor.Scoped<TService, TImpl>();

    // ── 用例 ──────────────────────────────────────────────────────────────

    [Fact]
    public void AuditAndReport_WithNullInputs_ShouldReturnEmpty()
    {
        var violations = ModuleDependencyAuditor.AuditAndReport(null!, null!);
        Assert.Empty(violations);
    }

    [Fact]
    public void AuditAndReport_WithEmptyModules_ShouldReturnEmpty()
    {
        var violations = ModuleDependencyAuditor.AuditAndReport(
            new List<IModuleDescriptor>(),
            new Dictionary<Type, List<ServiceDescriptor>>());
        Assert.Empty(violations);
    }

    /// <summary>
    /// 正向路径：真的缺声明就必须报出来。此前三个用例全是「应返回空」，
    /// 审计器整个失灵也不会有任何一条变红。
    /// </summary>
    [Fact]
    public void UndeclaredCrossModuleDependency_IsReported()
    {
        var a = new FakeDescriptor(typeof(ModuleA));           // 不依赖 B
        var b = new FakeDescriptor(typeof(ModuleB));

        var violations = ModuleDependencyAuditor.AuditAndReport(
            [a, b],
            new Dictionary<Type, List<ServiceDescriptor>>
            {
                [typeof(ModuleA)] = [ServiceDescriptor.Scoped<ConsumerNeedingB, ConsumerNeedingB>()],
                [typeof(ModuleB)] = [Registers<IServiceFromB, ServiceB>()],
            });

        var v = Assert.Single(violations);
        Assert.Equal(typeof(ModuleA), v.ConsumerModule);
        Assert.Equal(typeof(IServiceFromB), v.ServiceType);
        Assert.Equal(typeof(ModuleB), v.ProviderModule);
    }

    [Fact]
    public void DeclaredDependency_IsNotReported()
    {
        var b = new FakeDescriptor(typeof(ModuleB));
        var a = new FakeDescriptor(typeof(ModuleA), b);        // A [DependsOn] B

        var violations = ModuleDependencyAuditor.AuditAndReport(
            [a, b],
            new Dictionary<Type, List<ServiceDescriptor>>
            {
                [typeof(ModuleA)] = [ServiceDescriptor.Scoped<ConsumerNeedingB, ConsumerNeedingB>()],
                [typeof(ModuleB)] = [Registers<IServiceFromB, ServiceB>()],
            });

        Assert.Empty(violations);
    }

    /// <summary>
    /// 规则 1：一个服务类型可能有多个注册者（<c>CachingModule</c> 注册 <c>ICache</c>，
    /// <c>RedisCachingModule</c> 之后 <c>RemoveAll</c> 再注册），**任一**在依赖闭包内即放行。
    /// </summary>
    /// <remarks>
    /// 回归的是「后注册覆盖先注册」那个 bug：归属只记最后一个注册者时，本用例会把
    /// 提供者认定为 ModuleB，而消费方只声明了 ModuleA，于是误报。
    /// </remarks>
    [Fact]
    public void ServiceRegisteredByMultipleModules_PassesWhenAnyProviderIsDeclared()
    {
        var a = new FakeDescriptor(typeof(ModuleA));
        var b = new FakeDescriptor(typeof(ModuleB));
        var consumer = new FakeDescriptor(typeof(ConsumerModule), a);   // 只声明了 A

        var violations = ModuleDependencyAuditor.AuditAndReport(
            [a, b, consumer],
            new Dictionary<Type, List<ServiceDescriptor>>
            {
                // A 先注册；B 后注册同一个服务类型（模拟 Redis 替换 ICache）
                [typeof(ModuleA)] = [Registers<IServiceFromB, ServiceB>()],
                [typeof(ModuleB)] = [Registers<IServiceFromB, ServiceB2>()],
                [typeof(ConsumerModule)] = [ServiceDescriptor.Scoped<ConsumerNeedingB, ConsumerNeedingB>()],
            });

        Assert.Empty(violations);
    }

    /// <summary>
    /// 规则 2：可选构造参数（<c>IFoo? foo = null</c>）是框架表达可选依赖的既定写法，
    /// 提供方没加载就注入 null 优雅退化，不构成必须声明的依赖。
    /// </summary>
    [Fact]
    public void OptionalConstructorParameter_IsNotADependency()
    {
        var a = new FakeDescriptor(typeof(ModuleA));           // 不依赖 B
        var b = new FakeDescriptor(typeof(ModuleB));

        var violations = ModuleDependencyAuditor.AuditAndReport(
            [a, b],
            new Dictionary<Type, List<ServiceDescriptor>>
            {
                [typeof(ModuleA)] = [ServiceDescriptor.Scoped<ConsumerWithOptionalB, ConsumerWithOptionalB>()],
                [typeof(ModuleB)] = [Registers<IServiceFromB, ServiceB>()],
            });

        Assert.Empty(violations);
    }

    /// <summary>
    /// 规则 3：核心程序集（<c>Tnzi</c>）里那批模块随 <c>TnziApplication</c> 无条件加载，
    /// 不在任何 <c>[DependsOn]</c> 里，也不该要求声明。
    /// </summary>
    [Fact]
    public void ServiceFromAlwaysLoadedCoreModule_NeedsNoDeclaration()
    {
        // CachingModule 就住在核心程序集里
        var core = new FakeDescriptor(typeof(CachingModule));
        var a = new FakeDescriptor(typeof(ModuleA));           // 不声明依赖核心模块

        var violations = ModuleDependencyAuditor.AuditAndReport(
            [core, a],
            new Dictionary<Type, List<ServiceDescriptor>>
            {
                [typeof(CachingModule)] = [Registers<IServiceFromA, ServiceA>()],
                [typeof(ModuleA)] = [ServiceDescriptor.Scoped<ConsumerNeedingA, ConsumerNeedingA>()],
            });

        Assert.Empty(violations);
    }

    private sealed class ConsumerModule : TnziCustomModule;

    private sealed class ServiceA : IServiceFromA;

    private sealed class ServiceB : IServiceFromB;

    private sealed class ServiceB2 : IServiceFromB;

    private sealed class ConsumerNeedingA
    {
        public ConsumerNeedingA(IServiceFromA _) { }
    }
}
