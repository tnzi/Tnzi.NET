namespace Tnzi.Modules;

/// <summary>
/// 模块热重载配置选项
/// </summary>
public class ModuleHotReloadOptions
{
    /// <summary>
    /// 是否启用模块热重载
    /// 默认值：false（开发环境建议启用，生产环境不建议启用）
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 监听的文件扩展名
    /// 默认值：["*.dll"]
    /// </summary>
    public string[] WatchedFileExtensions { get; set; } = ["*.dll"];

    /// <summary>
    /// 监听的目录路径。相对路径按应用程序根目录（<see cref="AppContext.BaseDirectory"/>）解析，不按进程工作目录。
    /// 默认值：null（监听应用程序根目录）
    /// </summary>
    public string? WatchDirectory { get; set; }

    /// <summary>
    /// 文件变化后延迟重载的时间（毫秒）
    /// 用于避免频繁的文件变化导致多次重载
    /// 默认值：1000
    /// </summary>
    public int ReloadDelayMs { get; set; } = 1000;

    /// <summary>
    /// 是否包含子目录
    /// 默认值：true
    /// </summary>
    public bool IncludeSubdirectories { get; set; } = true;

    /// <summary>
    /// 排除的目录（相对于监听目录）
    /// 默认值：["bin", "obj", "node_modules", ".git"]
    /// </summary>
    public string[] ExcludedDirectories { get; set; } = ["bin", "obj", "node_modules", ".git"];
}
