namespace Tnzi;

/// <summary>
/// <see cref="ITnziApplication"/> 扩展方法
/// </summary>
public static class TnziApplicationExtensions
{
    /// <summary>
    /// 检查指定模块是否已加载
    /// </summary>
    /// <typeparam name="T">模块类型</typeparam>
    /// <param name="app">应用程序实例</param>
    /// <returns>如果模块已加载则返回 true</returns>
    public static bool IsModuleLoaded<T>(this ITnziApplication app) where T : ITnziModule
    {
        Check.NotNull(app);
        return app.Modules.Any(m => m.Type == typeof(T));
    }
}
