namespace Tnzi.EFCore.Options;

/// <summary>
/// 数据库配置选项
/// </summary>
public class DatabaseOptions
{
    /// <summary>
    /// 是否自动发现并注册所有 DbContext（基于配置文件）
    /// 默认值：true
    /// 
    /// 行为说明：
    /// - 当设置为 true（默认）时：
    ///   1. 从配置文件（Database:DbContexts）读取并验证 DbContext 配置
    ///   2. 如果配置无效或缺失，将抛出异常
    ///   3. 如果配置有效，自动注册所有 DbContext
    /// 
    /// - 当设置为 false 时：
    ///   1. 仍然会从配置文件读取 DbContext 配置（如果存在）
    ///   2. 如果配置有效，会自动注册（这是为了支持配置文件驱动的场景）
    ///   3. 如果配置无效或缺失，不会抛出异常（允许在代码中完全手动注册）
    ///   4. 如果需要在代码中完全手动注册，可以不配置 Database:DbContexts 部分
    /// </summary>
    public bool AutoDiscoverDbContexts { get; set; } = true;

    /// <summary>
    /// 是否自动发现并注册所有 IDataSeeder 实现
    /// 默认值：true
    /// </summary>
    public bool AutoRegisterDataSeeders { get; set; } = true;

    /// <summary>
    /// 主 DbContext 名称（对应 DbContexts 中某个配置的 Name）
    /// 如果未指定，将使用第一个 DbContext 作为主 DbContext
    /// </summary>
    public string? PrimaryDbContext { get; set; }

    /// <summary>
    /// DbContext 配置列表
    /// </summary>
    public List<DbContextConfiguration> DbContexts { get; set; } = new();
}

