
namespace Tnzi.EFCore.Internal;

/// <summary>
/// 设计时模块发现辅助类
/// 提供 DependsOn 依赖树构建，供 DesignTimeDbContextFactoryBase 和 TnziDbContextHelper 共享
/// </summary>
internal static class DesignTimeModuleDiscovery
{
    /// <summary>
    /// 查找启动模块类型：优先 DbContext 程序集，其次 EntryAssembly，最后 DbContext 引用的程序集
    /// </summary>
    public static Type? FindStartupModuleType(Assembly contextAssembly, Type[] allModuleTypes)
    {
        var inContextAssembly = allModuleTypes.Where(t => t.Assembly == contextAssembly).ToList();
        if (inContextAssembly.Count > 0)
        {
            var concrete = inContextAssembly.Where(t => !t.IsAbstract).ToList();
            if (concrete.Count == 1) return concrete[0];
            if (concrete.Count > 1)
            {
                var leaf = concrete.FirstOrDefault(c => !concrete.Any(o => o != c && c.IsAssignableFrom(o)));
                return leaf ?? concrete[0];
            }
        }

        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly != null)
        {
            var inEntry = allModuleTypes.Where(t => t.Assembly == entryAssembly && !t.IsAbstract).ToList();
            if (inEntry.Count == 1) return inEntry[0];
            if (inEntry.Count > 1)
            {
                var leaf = inEntry.FirstOrDefault(c => !inEntry.Any(o => o != c && c.IsAssignableFrom(o)));
                return leaf ?? inEntry[0];
            }
        }

        // 3. DbContext 程序集引用的程序集（DbContext 在独立 Data 项目、Startup 在 Api 项目时）
        foreach (var refName in contextAssembly.GetReferencedAssemblies())
        {
            if (AssemblyScanner.IsSystemOrThirdPartyAssembly(refName.Name ?? string.Empty))
                continue;

            Assembly? refAssembly;
            try
            {
                refAssembly = Assembly.Load(refName);
            }
            catch
            {
                continue;
            }

            var inRef = allModuleTypes.Where(t => t.Assembly == refAssembly && !t.IsAbstract).ToList();
            if (inRef.Count == 1) return inRef[0];
            if (inRef.Count > 1)
            {
                var leaf = inRef.FirstOrDefault(c => !inRef.Any(o => o != c && c.IsAssignableFrom(o)));
                return leaf ?? inRef[0];
            }
        }

        return null;
    }

    /// <summary>
    /// 按 DependsOn 递归填充模块树，容错处理（解析失败的依赖跳过，不抛异常）
    /// </summary>
    public static void FillDependsOnTree(
        Type moduleType,
        Dictionary<Type, ModuleDescriptor> modulesDict,
        HashSet<Type> visiting)
    {
        if (visiting.Contains(moduleType)) return;
        if (modulesDict.ContainsKey(moduleType)) return;

        visiting.Add(moduleType);
        try
        {
            if (!typeof(ITnziModule).IsAssignableFrom(moduleType))
                return;

            ITnziModule? module;
            try
            {
                module = Activator.CreateInstance(moduleType) as ITnziModule;
            }
            catch
            {
                return;
            }

            if (module == null) return;

            var descriptor = new ModuleDescriptor(moduleType, module);
            modulesDict[moduleType] = descriptor;

            var dependsOnAttributes = moduleType.GetCustomAttributes<DependsOnAttribute>(inherit: true);
            foreach (var attr in dependsOnAttributes)
            {
                foreach (var depType in attr.DependedModuleTypes ?? Array.Empty<Type>())
                {
                    if (depType == null) continue;
                    if (!typeof(ITnziModule).IsAssignableFrom(depType)) continue;

                    try
                    {
                        FillDependsOnTree(depType, modulesDict, visiting);
                        if (modulesDict.TryGetValue(depType, out var depDesc))
                        {
                            descriptor.AddDependency(depDesc);
                        }
                    }
                    catch (TypeLoadException)
                    {
                        // 依赖模块类型无法加载，跳过
                    }
                    catch
                    {
                        // 其他异常也跳过，保持容错
                    }
                }
            }
        }
        finally
        {
            visiting.Remove(moduleType);
        }
    }
}