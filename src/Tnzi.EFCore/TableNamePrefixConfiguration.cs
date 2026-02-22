
namespace Tnzi.EFCore;

/// <summary>
/// 表名前缀配置
/// </summary>
public class TableNamePrefixConfiguration : IEntityBatchConfiguration
{
    private readonly IModuleContainer? _moduleContainer;

    /// <summary>
    /// Design-Time 模式下的模块容器（静态属性，由 DesignTimeDbContextFactoryBase 设置）
    /// 用于在迁移工具运行时传递模块信息
    /// </summary>
    public static IModuleContainer? DesignTimeModuleContainer { get; set; }

    /// <summary>
    /// 初始化一个<see cref="TableNamePrefixConfiguration"/>类型的新实例
    /// </summary>
    public TableNamePrefixConfiguration(IModuleContainer? moduleContainer = null)
    {
        _moduleContainer = moduleContainer;
    }

    /// <summary>
    /// 配置指定的<see cref="IMutableEntityType"/>
    /// </summary>
    public void Configure(ModelBuilder modelBuilder, IMutableEntityType mutableEntityType)
    {
        ConfigureWithModuleContainer(modelBuilder, mutableEntityType, _moduleContainer);
    }

    /// <summary>
    /// 使用指定的 IModuleContainer 配置实体类型
    /// </summary>
    internal void ConfigureWithModuleContainer(ModelBuilder modelBuilder, IMutableEntityType mutableEntityType, IModuleContainer? moduleContainer)
    {
        string? prefix = GetTableNamePrefix(mutableEntityType.ClrType, moduleContainer);
        if (string.IsNullOrEmpty(prefix))
        {
            // 如果无法获取前缀，记录警告（仅在非 Design-Time 模式下）
            if (moduleContainer != null || DesignTimeModuleContainer != null)
            {
                // 有 moduleContainer 但无法获取前缀，可能是模块匹配问题
                // 这里不记录日志，因为可能是正常的（某些实体不需要前缀）
            }
            return;
        }

        // 获取当前表名（当前采用单数形式）
        string baseTableName = mutableEntityType.GetTableName() ?? mutableEntityType.ClrType.Name;

        // 如果表名已经包含前缀，不再重复添加
        if (baseTableName.StartsWith($"{prefix}_", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 如果实体类名等于模块前缀（如 Template 实体在 Template 模块中），避免重复前缀
        // 直接使用前缀作为表名，而不是 {prefix}_{prefix}
        string tableName = string.Equals(baseTableName, prefix, StringComparison.OrdinalIgnoreCase)
            ? prefix
            : $"{prefix}_{baseTableName}";
        modelBuilder.Entity(mutableEntityType.ClrType).ToTable(tableName);
    }

    /// <summary>
    /// 从实体类型获取表名前缀
    /// 通过实体所在的程序集查找对应的模块，获取模块的 TableNamePrefix 属性
    /// </summary>
    protected virtual string? GetTableNamePrefix(Type entityType, IModuleContainer? moduleContainer = null)
    {
        // 优先级：
        // 1. 传入的 moduleContainer 参数
        // 2. 构造函数中的 _moduleContainer
        // 3. Design-Time 模式下的静态 DesignTimeModuleContainer
        moduleContainer ??= _moduleContainer ?? DesignTimeModuleContainer;

        if (moduleContainer == null)
        {
            return null;
        }

        // 获取实体所在的程序集
        var assembly = entityType.Assembly;
        var assemblyName = assembly.GetName().Name;

        // 查找该程序集中的模块（通常只有一个 TnziApplicationModule）
        // 注意：需要匹配程序集，并且模块实例必须是 TnziApplicationModule 类型
        var module = moduleContainer.Modules
            .FirstOrDefault(m =>
            {
                var moduleAssembly = m.Assembly;
                var moduleAssemblyName = moduleAssembly?.GetName().Name;
                // 匹配逻辑：模块所在程序集与实体所在程序集相同，或者模块类型所在程序集与实体所在程序集相同
                var isSameAssembly = moduleAssembly == assembly || string.Equals(moduleAssemblyName, assemblyName, StringComparison.OrdinalIgnoreCase);

                if (!isSameAssembly)
                {
                    var moduleTypeAssembly = m.Type.Assembly;
                    var moduleTypeAssemblyName = moduleTypeAssembly.GetName().Name;
                    isSameAssembly = moduleTypeAssembly == assembly || string.Equals(moduleTypeAssemblyName, assemblyName, StringComparison.OrdinalIgnoreCase);
                }
                var isApplicationModule = m.Instance is TnziApplicationModule;
                var isCustomModule = m.Instance is TnziCustomModule;
                return isSameAssembly && (isApplicationModule || isCustomModule);
            });

        // 统一通过 IHasTableNamePrefix 接口获取前缀（不再使用反射）
        if (module?.Instance is IHasTableNamePrefix prefixModule)
        {
            return prefixModule.TableNamePrefix;
        }

        // 如果找不到匹配的模块，返回 null（不添加前缀）
        return null;
    }
}