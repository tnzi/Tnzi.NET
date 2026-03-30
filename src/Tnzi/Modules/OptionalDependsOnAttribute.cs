namespace Tnzi.Modules;

/// <summary>
/// 声明模块的可选依赖
/// 如果目标模块存在，则确保其先于当前模块加载
/// 如果目标模块不存在，则静默忽略（不报错）
/// </summary>
[StableApi(Since = "0.1.0")]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class OptionalDependsOnAttribute : Attribute
{
    public Type[] DependedModuleTypes { get; }

    public OptionalDependsOnAttribute(params Type[] dependedModuleTypes)
    {
        DependedModuleTypes = dependedModuleTypes ?? [];
    }
}
