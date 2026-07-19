using System.Reflection;

namespace Tnzi.TestBase;

/// <summary>
/// 约定测试的反射辅助
/// </summary>
public static class ReflectionTestExtensions
{
    /// <summary>
    /// 取程序集全部类型；任一类型的签名引用了不可加载的依赖时降级返回可加载子集，
    /// 而非让 <see cref="ReflectionTypeLoadException"/> 把约定测试变成加载崩溃
    /// </summary>
    /// <remarks>
    /// 收口 AdminWriteEndpointPermissionConventionTests / EntityPolicyTests /
    /// FrameworkAssemblyConventionTests 各自私有 SafeGetTypes 的共享版；
    /// 新约定测试一律用本扩展，存量拷贝随下次触碰迁移。
    /// </remarks>
    public static IReadOnlyList<Type> SafeGetTypes(this Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Cast<Type>().ToList();
        }
    }
}
