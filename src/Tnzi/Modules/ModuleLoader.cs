

namespace Tnzi.Modules;

/// <summary>
/// 模块加载器
/// 负责发现、排序和加载模块
/// </summary>
public class ModuleLoader
{
    // 模块工厂缓存（表达式树编译，替代反射）
    private static readonly ConcurrentDictionary<Type, Func<ITnziModule>> _moduleFactories = new();

    /// <summary>
    /// 「类型可解析但模块未加载」的可选依赖列表 —— <c>[OptionalDependsOn]</c> 目标的程序集已被引用
    /// （typeof 解析成功）但该模块不在启动模块的 <c>[DependsOn]</c> 闭包内，因此不会被加载。
    /// 加载期没有 logger（先于 DI 容器构建），由 <c>TnziApplication.InitializeAsync</c> 延迟聚合输出
    /// 诊断日志，帮助开发者发现「以为 OptionalDependsOn 会加载模块」的常见误解。
    /// </summary>
    public IReadOnlyList<(Type Consumer, Type Wanted)> SkippedOptionalDependencies { get; private set; } = [];

    /// <summary>
    /// 加载模块
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="startupModuleType">启动模块类型</param>
    /// <returns>排序后的模块描述符列表</returns>
    public IReadOnlyList<IModuleDescriptor> LoadModules(
        IServiceCollection services,
        Type startupModuleType)
    {
        var modules = GetDescriptors(services, startupModuleType);

        // 使用带优先级的拓扑排序（Kahn算法变体）
        // 在满足依赖的前提下，优先加载高优先级（数值小）的模块
        return TopologicalSortWithPriority(modules);
    }

    /// <summary>
    /// 获取模块描述符
    /// </summary>
    private List<IModuleDescriptor> GetDescriptors(
        IServiceCollection services,
        Type startupModuleType)
    {
        // 使用 Dictionary 进行 O(1) 查找，同时保持 List 以保留插入顺序
        var modulesDict = new Dictionary<Type, ModuleDescriptor>();
        var modulesList = new List<ModuleDescriptor>();
        var visiting = new HashSet<Type>(); // 用于检测循环依赖
        Fill(services, modulesDict, modulesList, startupModuleType, visiting, new List<Type>());

        // 第二遍：解析可选依赖（所有模块已加载完毕，避免处理顺序导致的遗漏）
        SkippedOptionalDependencies = ResolveOptionalDependencies(modulesDict, modulesList);

        return modulesList.Cast<IModuleDescriptor>().ToList();
    }

    /// <summary>
    /// 递归填充模块及其依赖
    /// </summary>
    private void Fill(
        IServiceCollection services,
        Dictionary<Type, ModuleDescriptor> modulesDict,
        List<ModuleDescriptor> modulesList,
        Type moduleType,
        HashSet<Type> visiting,
        List<Type> path)
    {
        // 检测循环依赖
        if (visiting.Contains(moduleType))
        {
            path.Add(moduleType);
            var cyclePath = path.SkipWhile(t => t != moduleType).ToList();
            var pathString = string.Join(" -> ", cyclePath.Select(t => t.Name));
            throw new ModuleCircularDependencyException(
                $"Circular dependency detected: {pathString}",
                cyclePath);
        }

        // 防止重复处理 - O(1) 查找
        if (modulesDict.ContainsKey(moduleType))
            return;

        visiting.Add(moduleType);
        path.Add(moduleType);

        // try/finally 确保无论正常返回还是异常向上传播，都会把当前模块从递归路径
        // （visiting/path）中移除。否则缺失依赖异常抛出后模块会残留在 visiting 中，
        // 使随后经由另一条路径抵达同一模块时被误判为循环依赖。
        try
        {
            // 使用缓存的工厂创建实例
            var factory = _moduleFactories.GetOrAdd(moduleType, BuildModuleFactory);
            var module = factory();

            var descriptor = new ModuleDescriptor(moduleType, module);
            modulesDict[moduleType] = descriptor;
            modulesList.Add(descriptor);

            // 处理必选依赖
            var dependsOnAttributes = moduleType.GetCustomAttributes<DependsOnAttribute>();
            var missingDependencies = new List<Type>();

            foreach (var attribute in dependsOnAttributes)
            {
                foreach (var dependedModuleType in attribute.DependedModuleTypes)
                {
                    // 检查依赖模块类型是否有效
                    if (dependedModuleType == null)
                    {
                        missingDependencies.Add(typeof(object));
                        continue;
                    }

                    if (!typeof(ITnziModule).IsAssignableFrom(dependedModuleType))
                    {
                        missingDependencies.Add(dependedModuleType);
                        continue;
                    }

                    // 尝试加载依赖模块。
                    // 注意：不再捕获 ModuleMissingDependencyException —— 子模块自身缺失依赖时
                    // 让异常直接向上传播（fail-fast）。旧实现吞掉该异常并声称"最终会在当前模块
                    // 检查时抛出"，但子模块此时已进入 modulesDict，下面的 TryGetValue 会命中，
                    // 当前模块的 missingDependencies 保持为空，从而静默加载了残缺的模块图。
                    try
                    {
                        Fill(services, modulesDict, modulesList, dependedModuleType, visiting, path);
                    }
                    catch (TypeLoadException)
                    {
                        // 依赖模块类型无法加载（程序集缺失等），记录为缺失依赖
                        missingDependencies.Add(dependedModuleType);
                        continue;
                    }

                    // O(1) 查找依赖模块
                    if (modulesDict.TryGetValue(dependedModuleType, out var dependedDescriptor))
                    {
                        descriptor.AddDependency(dependedDescriptor);
                    }
                    else
                    {
                        // 依赖模块加载失败，记录为缺失依赖
                        missingDependencies.Add(dependedModuleType);
                    }
                }
            }

            // 如果存在缺失依赖，抛出异常（错误信息携带从启动模块到当前模块的完整依赖链）
            if (missingDependencies.Count > 0)
            {
                var missingNames = string.Join(", ", missingDependencies.Select(t => t.Name));
                var chain = string.Join(" -> ", path.Select(t => t.Name));
                throw new ModuleMissingDependencyException(
                    moduleType,
                    $"Module {moduleType.Name} has missing dependencies: {missingNames}. Dependency chain: {chain}",
                    missingDependencies);
            }
        }
        finally
        {
            visiting.Remove(moduleType);
            path.RemoveAt(path.Count - 1);
        }
    }

    /// <summary>
    /// 解析可选依赖（第二遍遍历）
    /// 在所有模块加载完毕后统一处理，避免 Fill() 递归顺序导致可选依赖遗漏
    /// </summary>
    /// <returns>「类型可解析但模块未加载」的可选依赖（消费者模块, 期望模块）对列表</returns>
    private static List<(Type Consumer, Type Wanted)> ResolveOptionalDependencies(
        Dictionary<Type, ModuleDescriptor> modulesDict,
        List<ModuleDescriptor> modulesList)
    {
        var skipped = new List<(Type Consumer, Type Wanted)>();

        foreach (var descriptor in modulesList)
        {
            IEnumerable<OptionalDependsOnAttribute> optionalAttributes;
            try
            {
                // GetCustomAttributes may throw FileNotFoundException when the attribute
                // references a type (typeof()) from an assembly that is not loaded.
                // This is expected for optional dependencies — the assembly may not be present.
                optionalAttributes = descriptor.Type.GetCustomAttributes<OptionalDependsOnAttribute>();
            }
            catch (FileNotFoundException)
            {
                continue;
            }

            foreach (var attribute in optionalAttributes)
            {
                foreach (var dependedModuleType in attribute.DependedModuleTypes)
                {
                    if (dependedModuleType == null || !typeof(ITnziModule).IsAssignableFrom(dependedModuleType))
                        continue;

                    // 模块已加载则添加依赖边（确保加载顺序）；
                    // 类型可解析但模块未加载 → 记录下来供启动期聚合诊断（OptionalDependsOn 只排序、不加载）
                    if (modulesDict.TryGetValue(dependedModuleType, out var dependedDescriptor))
                    {
                        descriptor.AddDependency(dependedDescriptor);
                    }
                    else
                    {
                        skipped.Add((descriptor.Type, dependedModuleType));
                    }
                }
            }
        }

        return skipped;
    }

    /// <summary>
    /// 构建模块工厂（使用表达式树替代反射，提升性能）
    /// </summary>
    private static Func<ITnziModule> BuildModuleFactory(Type moduleType)
    {
        var ctor = moduleType.GetConstructor(Type.EmptyTypes);
        if (ctor == null)
            throw new InvalidOperationException($"Module {moduleType.Name} must have a parameterless constructor");
        
        var newExpr = Expression.New(ctor);
        var lambda = Expression.Lambda<Func<ITnziModule>>(newExpr);
        return lambda.Compile();
    }
    
    /// <summary>
    /// 带优先级的拓扑排序（Kahn算法变体）
    /// 在每一轮选择入度为0的节点时，优先选择优先级最高（数值最小）的节点
    /// 这样可以在满足依赖约束的前提下，让高优先级模块先加载
    /// </summary>
    private List<IModuleDescriptor> TopologicalSortWithPriority(List<IModuleDescriptor> modules)
    {
        var result = new List<IModuleDescriptor>();
        var inDegree = new Dictionary<IModuleDescriptor, int>();
        var dependents = new Dictionary<IModuleDescriptor, List<IModuleDescriptor>>();
        
        // 初始化入度和依赖者列表
        foreach (var module in modules)
        {
            inDegree[module] = module.Dependencies?.Count ?? 0;
            dependents[module] = new List<IModuleDescriptor>();
        }
        
        // 建立反向依赖图（谁依赖了谁）
        foreach (var module in modules)
        {
            if (module.Dependencies != null)
            {
                foreach (var dep in module.Dependencies)
                {
                    if (dependents.ContainsKey(dep))
                    {
                        dependents[dep].Add(module);
                    }
                }
            }
        }
        
        // 创建比较器：按绝对加载顺序排序，相同时按类型名排序（确保确定性）
        var comparer = Comparer<IModuleDescriptor>.Create((a, b) =>
        {
            var orderA = TnziModuleHelper.GetAbsoluteLoadOrder(a.Instance);
            var orderB = TnziModuleHelper.GetAbsoluteLoadOrder(b.Instance);
            var cmp = orderA.CompareTo(orderB);
            return cmp != 0 ? cmp : string.Compare(a.Type.FullName, b.Type.FullName, StringComparison.Ordinal);
        });
        
        // 使用 SortedSet 作为优先队列，初始化为所有入度为0的节点
        var ready = new SortedSet<IModuleDescriptor>(
            modules.Where(m => inDegree[m] == 0),
            comparer);
        
        while (ready.Count > 0)
        {
            // 取出优先级最高（数值最小）的模块
            var current = ready.Min!;
            ready.Remove(current);
            result.Add(current);
            
            // 更新所有依赖当前模块的模块的入度
            foreach (var dependent in dependents[current])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                {
                    ready.Add(dependent);
                }
            }
        }
        
        // 检查是否存在循环依赖（备用检测，理论上在Fill中已检测）
        if (result.Count != modules.Count)
        {
            var unprocessed = modules.Where(m => !result.Contains(m)).ToList();
            var cycleModules = string.Join(", ", unprocessed.Select(m => m.Type.Name));
            
            throw new ModuleCircularDependencyException(
                $"Circular dependency detected in module loading. The following modules have unresolved dependencies: {cycleModules}",
                unprocessed.Select(m => m.Type).ToList());
        }
        
        return result;
    }
}