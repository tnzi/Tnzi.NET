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
    /// 默认值：true。Production 环境默认被 SkipDatabaseInitInProduction 跳过；
    /// 未注册任何 IDbInitializer 时为 no-op。
    /// 可经配置节 "Tnzi:AutoInitializeDatabase" 或代码回调关闭。
    /// </summary>
    public bool AutoInitializeDatabase { get; set; } = true;

    /// <summary>
    /// 数据库初始化失败时是否中断应用启动
    /// 默认值：true（数据库初始化失败时抛出异常）
    /// </summary>
    public bool FailFastOnDatabaseInitError { get; set; } = true;

    /// <summary>
    /// 是否在生产环境跳过数据库初始化检查（迁移 + 种子整体开关，<b>旧选项</b>）
    /// 默认值：true（生产环境跳过，提升启动性能）
    /// </summary>
    /// <remarks>
    /// 保留以向后兼容。细粒度控制请用正交的 <see cref="ApplyMigrationsInProduction"/>
    /// （迁移，幂等安全）与 <see cref="SeedInProduction"/>（种子，可能改数据）——
    /// 这两个新选项未显式配置时回退到本开关的语义，故现有 appsettings 行为不变。
    /// </remarks>
    public bool SkipDatabaseInitInProduction { get; set; } = true;

    /// <summary>
    /// 生产环境是否执行数据库迁移（仅 <c>MigrateAsync</c>，幂等安全）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="SeedInProduction"/> 正交，把「迁移」从「种子」里拆出来，
    /// 让生产可以「只迁移不种子」。
    /// <para><b>默认 <c>null</c> = 回退到 <see cref="SkipDatabaseInitInProduction"/></b>：
    /// 未配置时沿用旧的整体开关（默认跳过），保持现有部署行为不变；
    /// 显式设为 <c>true</c> 即在生产执行幂等迁移（种子仍由 <see cref="SeedInProduction"/> 决定）。</para>
    /// <para>注意：多副本并发迁移无分布式锁，多副本部署应把迁移交给 CI/CD 流程而非在此开启。</para>
    /// </remarks>
    public bool? ApplyMigrationsInProduction { get; set; }

    /// <summary>
    /// 生产环境是否执行种子数据初始化（<c>IDataSeeder</c>）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ApplyMigrationsInProduction"/> 正交。种子可能写入/覆盖数据，生产默认关闭。
    /// <para><b>默认 <c>null</c> = 回退到 <see cref="SkipDatabaseInitInProduction"/></b>：
    /// 未配置时沿用旧的整体开关，保持现有部署行为不变。</para>
    /// </remarks>
    public bool? SeedInProduction { get; set; }

    /// <summary>
    /// 依据环境判定是否执行数据库迁移。非生产环境恒 <c>true</c>；生产环境按
    /// <see cref="ApplyMigrationsInProduction"/> 决定，未配置时回退到旧的
    /// <see cref="SkipDatabaseInitInProduction"/>（保持现有行为）。
    /// </summary>
    /// <param name="isProduction">当前是否为生产环境</param>
    public bool ShouldApplyMigrations(bool isProduction)
        => !isProduction || (ApplyMigrationsInProduction ?? !SkipDatabaseInitInProduction);

    /// <summary>
    /// 依据环境判定是否执行种子数据初始化。非生产环境恒 <c>true</c>；生产环境按
    /// <see cref="SeedInProduction"/> 决定，未配置时回退到旧的
    /// <see cref="SkipDatabaseInitInProduction"/>（保持现有行为）。
    /// </summary>
    /// <param name="isProduction">当前是否为生产环境</param>
    public bool ShouldSeed(bool isProduction)
        => !isProduction || (SeedInProduction ?? !SkipDatabaseInitInProduction);

    /// <summary>
    /// 是否启用模块依赖审计
    /// 启用后会在启动时分析模块间的服务依赖，对未声明 [DependsOn] 的跨模块依赖输出警告
    /// 建议仅在开发环境启用
    /// 默认值：false
    /// </summary>
    public bool EnableModuleDependencyAudit { get; set; } = false;

    /// <summary>
    /// 是否启用运行时设置消费审计
    /// 启动时检测「标记 [RuntimeSetting]（可热设置）的 Options 却被 IOptions&lt;T&gt; 启动快照消费」
    /// 的沉默失败（admin 改了不生效）。与模块依赖审计不同，此审计噪音低、命中即真问题，因此独立门控且默认开启
    /// 默认值：true
    /// </summary>
    public bool EnableRuntimeSettingConsumerAudit { get; set; } = true;

    /// <summary>
    /// 启动失败时是否写入错误日志文件 (startup-error.log)
    /// 文件写入 AppContext.BaseDirectory，适用于 IIS/Docker/Windows Service 等控制台输出不可见的场景
    /// 默认值：true
    /// </summary>
    public bool WriteStartupErrorLog { get; set; } = true;
}
