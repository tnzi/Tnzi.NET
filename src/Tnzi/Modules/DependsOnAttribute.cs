namespace Tnzi.Modules;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
[StableApi(Since = "0.1.0")]
public class DependsOnAttribute : Attribute
{
    public Type[] DependedModuleTypes { get; }

    public DependsOnAttribute(params Type[] dependedModuleTypes)
    {
        DependedModuleTypes = dependedModuleTypes ?? [];
    }
}

