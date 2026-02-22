
namespace Tnzi.HealthChecks.Checks;

/// <summary>
/// 已注册的 DbContext 类型列表
/// 在 HealthChecksModule 中通过扫描 IServiceCollection 自动填充
/// </summary>
public sealed class RegisteredDbContextTypes
{
    /// <summary>
    /// 所有已注册的 DbContext 类型
    /// </summary>
    public List<Type> Types { get; } = new();
}

/// <summary>
/// 数据库健康检查
/// 用于检测数据库连接状态
/// 支持多 DbContext 场景：自动发现并检查所有已注册的 DbContext
/// </summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RegisteredDbContextTypes _dbContextTypes;

    public DatabaseHealthCheck(IServiceProvider serviceProvider, RegisteredDbContextTypes dbContextTypes)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _dbContextTypes = Check.NotNull(dbContextTypes);
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_dbContextTypes.Types.Count == 0)
            {
                return HealthCheckResult.Unhealthy(
                    "No DbContext registered. Make sure EFCoreModule is configured correctly.");
            }

            using var scope = _serviceProvider.CreateScope();
            var allData = new Dictionary<string, object>();
            var unhealthyContexts = new List<string>();
            var checkedCount = 0;

            foreach (var dbContextType in _dbContextTypes.Types)
            {
                var dbContext = scope.ServiceProvider.GetService(dbContextType) as DbContext;
                if (dbContext == null)
                    continue;

                checkedCount++;
                var contextName = dbContextType.Name;

                try
                {
                    var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
                    var providerName = dbContext.Database.ProviderName ?? "Unknown";

                    allData[$"{contextName}.Provider"] = providerName;
                    allData[$"{contextName}.Status"] = canConnect ? "Connected" : "Failed";

                    if (!canConnect)
                    {
                        unhealthyContexts.Add(contextName);
                    }
                }
                catch (Exception ex)
                {
                    allData[$"{contextName}.Status"] = "Error";
                    allData[$"{contextName}.Error"] = ex.Message;
                    unhealthyContexts.Add(contextName);
                }
            }

            if (checkedCount == 0)
            {
                return HealthCheckResult.Unhealthy(
                    "No DbContext could be resolved. Make sure EFCoreModule is configured correctly.");
            }

            allData["TotalDbContexts"] = checkedCount;

            if (unhealthyContexts.Count > 0)
            {
                return HealthCheckResult.Unhealthy(
                    $"Database connection failed for: {string.Join(", ", unhealthyContexts)}",
                    data: allData);
            }

            return HealthCheckResult.Healthy(
                checkedCount == 1
                    ? "Database connection is healthy"
                    : $"All {checkedCount} database connections are healthy",
                allData);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Database check failed. Check connection string and database availability.",
                ex);
        }
    }
}
