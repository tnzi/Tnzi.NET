namespace Tnzi.EFCore.Options;

/// <summary>
/// Tnzi EFCore 模块配置选项
/// 配置路径：EFCore
/// 
/// 注意：DbContext 相关的配置（AutoDiscoverDbContexts、AutoRegisterDataSeeders、PrimaryDbContext、DbContexts）
/// 位于 Database 配置节点，请使用 DatabaseOptions。
/// </summary>
public class EFCoreOptions
{
    /// <summary>
    /// 是否在 Repository 方法中自动保存更改
    /// 默认值：true（与旧框架行为一致）
    /// 
    /// 当为 true 时，Repository 的 InsertAsync、UpdateAsync、DeleteAsync 等方法会自动调用 SaveChangesAsync
    /// 当为 false 时，需要手动调用 SaveChangesAsync 或依赖 UnitOfWorkFilter 自动提交
    /// </summary>
    public bool AutoSaveChanges { get; set; } = true;

    /// <summary>
    /// 是否启用慢查询日志
    /// 默认值：true
    /// </summary>
    public bool EnableSlowQueryLogging { get; set; } = true;

    /// <summary>
    /// 慢查询阈值（毫秒）
    /// 执行时间超过此值的查询将被记录
    /// 默认值：100ms
    /// </summary>
    public int SlowQueryThresholdMs { get; set; } = 100;

    /// <summary>
    /// 是否记录查询参数
    /// 默认值：false（生产环境建议关闭以保护敏感数据）
    /// </summary>
    public bool LogQueryParameters { get; set; } = false;

    /// <summary>
    /// 批量操作的默认批次大小
    /// 默认值：1000
    /// 
    /// 当批量插入、更新或删除的实体数量超过此值时，将分批处理以减少内存占用
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// 按实体类型配置的批次大小
    /// Key 为实体类型的完整名称（FullName），Value 为批次大小
    /// 
    /// 如果实体类型在此字典中，将使用指定的批次大小；否则使用 BatchSize
    /// 示例：{ "MyApp.Entities.Product", 500 } 表示 Product 实体的批次大小为 500
    /// </summary>
    public Dictionary<string, int> BatchSizes { get; set; } = new();

    /// <summary>
    /// 是否启用事务检测
    /// 默认值：true
    /// 
    /// 当为 true 时，Repository 会检测当前是否在事务中，如果在事务中则延迟保存更改
    /// 检测顺序：UnitOfWorkManager -> UnitOfWork -> DbContext.Database.CurrentTransaction
    /// </summary>
    public bool EnableTransactionDetection { get; set; } = true;

    /// <summary>
    /// 是否记录自动保存决策过程
    /// 默认值：false
    /// 
    /// 当为 true 时，会在 Debug 级别记录 ShouldSaveImmediately 的决策过程
    /// 用于调试和诊断自动保存行为
    /// </summary>
    public bool LogAutoSaveDecisions { get; set; } = false;
}

