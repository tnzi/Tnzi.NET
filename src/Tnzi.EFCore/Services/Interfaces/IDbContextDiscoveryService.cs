
namespace Tnzi.EFCore.Services;

/// <summary>
/// DbContext 发现服务接口
/// 负责自动发现和验证 DbContext 配置
/// </summary>
public interface IDbContextDiscoveryService
{
    /// <summary>
    /// 发现并验证所有配置的 DbContext
    /// </summary>
    /// <param name="options">数据库选项</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <returns>验证后的 DbContext 配置列表和主 DbContext</returns>
    DbContextDiscoveryResult DiscoverDbContexts(Options.DatabaseOptions options, ILogger? logger = null);
}

/// <summary>
/// DbContext 发现结果
/// </summary>
public class DbContextDiscoveryResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success => Configurations.Count > 0;

    /// <summary>
    /// 发现的 DbContext 配置列表
    /// </summary>
    public List<DbContextDiscoveryItem> Configurations { get; init; } = new();

    /// <summary>
    /// 主 DbContext
    /// </summary>
    public DbContextDiscoveryItem? PrimaryConfiguration { get; init; }

    /// <summary>
    /// 非主 DbContext
    /// </summary>
    public IEnumerable<DbContextDiscoveryItem> SecondaryConfigurations
        => Configurations.Where(c => c != PrimaryConfiguration);

    /// <summary>
    /// 验证错误列表（配置项名称 -> 错误原因）
    /// </summary>
    public Dictionary<string, string> ValidationErrors { get; init; } = new();

    /// <summary>
    /// 是否有配置但都无效
    /// </summary>
    public bool HasInvalidConfigurations => ValidationErrors.Count > 0;
}

/// <summary>
/// 单个 DbContext 发现项
/// </summary>
public class DbContextDiscoveryItem
{
    /// <summary>
    /// 配置信息
    /// </summary>
    public required Options.DbContextConfiguration Configuration { get; init; }

    /// <summary>
    /// DbContext 类型
    /// </summary>
    public required Type DbContextType { get; init; }
}