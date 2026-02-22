namespace Tnzi.Modules;

/// <summary>
/// 声明模块的可选依赖
/// 如果目标模块存在，则确保其先于当前模块加载
/// 如果目标模块不存在，则静默忽略（不报错）
/// </summary>
[ExperimentalApi(Reason = "可选依赖机制为新增特性，API 可能在后续版本中调整")]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class OptionalDependsOnAttribute : Attribute
{
    public Type[] DependedModuleTypes { get; }

    public OptionalDependsOnAttribute(params Type[] dependedModuleTypes)
    {
        DependedModuleTypes = dependedModuleTypes ?? [];
    }
}
