namespace Tnzi.Architecture.Tests;

/// <summary>
/// 全模块图的共享加载入口。
/// </summary>
/// <remarks>
/// 用 <see cref="Lazy{T}"/> 缓存：加载一次要跑 50 多个模块的三个配置阶段，四道门禁各加载一遍
/// 纯属浪费。结果只被读取（断言），不会被任何测试改写。
/// </remarks>
internal static class ArchitectureModuleGraph
{
    private static readonly Lazy<ModuleLoadResult> Cached = new(LoadCore, isThreadSafe: true);

    public static ModuleLoadResult Load() => Cached.Value;

    private static ModuleLoadResult LoadCore()
    {
        return ModuleTestHelper.LoadAndCollectServiceMap<AllModulesStartupModule>(
            new Dictionary<string, string?>
            {
                // EFCoreModule 的自动发现要求一个能解析到的 DbContext 类型，否则它整个出局，
                // 连带 IRepository<,> 不进服务图（注册发生在 AddTnziDbContext 内部）。
                ["Database:DbContexts:0:DbContextType"] =
                    typeof(ArchitectureTestDbContext).AssemblyQualifiedName,
            });
    }
}
