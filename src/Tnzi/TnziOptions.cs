namespace Tnzi;

/// <summary>
/// Tnzi.NET 框架全局配置选项
/// </summary>
public class TnziOptions
{
    /// <summary>
    /// 是否启用诊断模式
    /// 启用后会输出更详细的日志信息
    /// </summary>
    public bool EnableDiagnostics { get; set; } = false;

    /// <summary>
    /// 是否启用模块加载日志
    /// </summary>
    public bool LogModuleLoading { get; set; } = true;

    /// <summary>
    /// 是否使用 Source Generator 生成的模块注册表
    /// 启用后可以避免运行时反射扫描，提升启动性能
    /// </summary>
    public bool UseGeneratedModuleRegistry { get; set; } = false;

    /// <summary>
    /// 是否自动初始化数据库（迁移和种子数据）
    /// 默认值：false（需要手动调用 UseTnziWithDefaultsAsync）
    /// </summary>
    public bool AutoInitializeDatabase { get; set; } = false;

    /// <summary>
    /// 数据库初始化失败时是否中断应用启动
    /// 默认值：true（数据库初始化失败时抛出异常）
    /// </summary>
    public bool FailFastOnDatabaseInitError { get; set; } = true;

    /// <summary>
    /// 是否在生产环境跳过数据库初始化检查
    /// 默认值：true（生产环境跳过，提升启动性能）
    /// </summary>
    public bool SkipDatabaseInitInProduction { get; set; } = true;

    /// <summary>
    /// 是否启用模块依赖审计
    /// 启用后会在启动时分析模块间的服务依赖，对未声明 [DependsOn] 的跨模块依赖输出警告
    /// 建议仅在开发环境启用
    /// 默认值：false
    /// </summary>
    public bool EnableModuleDependencyAudit { get; set; } = false;
}
