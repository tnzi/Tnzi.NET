namespace Tnzi.Modules;

/// <summary>
/// 模块扩展方法
/// </summary>
public static class ModuleExtensions
{
    /// <summary>
    /// 检查模块是否已启用
    /// </summary>
    public static bool IsEnabled(this IModuleDescriptor module)
        => module.IsEnabled;

    /// <summary>
    /// 获取模块初始化耗时
    /// </summary>
    public static TimeSpan? GetInitializationDuration(this IModuleDescriptor module)
    {
        if (module is not ModuleDescriptor descriptor)
            return null;

        if (descriptor.InitializationStartTime == null || descriptor.InitializationEndTime == null)
            return null;

        return descriptor.InitializationEndTime.Value - descriptor.InitializationStartTime.Value;
    }

    /// <summary>
    /// 获取模块初始化开始时间
    /// </summary>
    public static DateTime? GetInitializationStartTime(this IModuleDescriptor module)
    {
        return module is ModuleDescriptor descriptor ? descriptor.InitializationStartTime : null;
    }

    /// <summary>
    /// 获取模块初始化完成时间
    /// </summary>
    public static DateTime? GetInitializationEndTime(this IModuleDescriptor module)
    {
        return module is ModuleDescriptor descriptor ? descriptor.InitializationEndTime : null;
    }
}

