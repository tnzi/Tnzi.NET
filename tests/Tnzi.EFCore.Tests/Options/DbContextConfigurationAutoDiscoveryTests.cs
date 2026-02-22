
namespace Tnzi.EFCore.Tests.Options;

/// <summary>
/// DbContextConfiguration AutoDiscovery 测试类
/// 通过 DbContextDiscoveryService 间接测试自动发现功能
/// </summary>
public class DbContextConfigurationAutoDiscoveryTests
{
    [Fact]
    public void DiscoverDbContexts_WithEmptyDbContextType_AutoDiscoversFromName()
    {
        // Arrange
        var service = new DbContextDiscoveryService();
        var options = new DatabaseOptions
        {
            AutoDiscoverDbContexts = true,
            DbContexts = new List<DbContextConfiguration>
            {
                new DbContextConfiguration
                {
                    Name = "Default",
                    DbContextType = "", // 空，应该自动发现
                    ConnectionString = "Host=localhost;Port=5432;Database=test",
                    Provider = DatabaseProvider.PostgreSQL // 显式指定 Provider
                }
            }
        };

        // Act
        var result = service.DiscoverDbContexts(options);

        // Assert
        // 应该成功发现 DefaultDbContext 类型
        Assert.True(result.Success);
        Assert.Single(result.Configurations);
        Assert.NotNull(result.PrimaryConfiguration);
        Assert.Equal("DefaultDbContext", result.PrimaryConfiguration!.DbContextType.Name);
    }

    [Fact]
    public void DiscoverDbContexts_WithNotFoundName_ReturnsError()
    {
        // Arrange
        var service = new DbContextDiscoveryService();
        var options = new DatabaseOptions
        {
            AutoDiscoverDbContexts = true,
            DbContexts = new List<DbContextConfiguration>
            {
                new DbContextConfiguration
                {
                    Name = "NonExistent",
                    DbContextType = "", // 空，应该尝试自动发现
                    ConnectionString = "Host=localhost;Port=5432;Database=test",
                    Provider = DatabaseProvider.PostgreSQL
                }
            }
        };

        // Act
        var result = service.DiscoverDbContexts(options);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.HasInvalidConfigurations);
        Assert.Contains("NonExistent", result.ValidationErrors.Keys);
        Assert.Contains("Cannot auto-discover", result.ValidationErrors["NonExistent"]);
    }
}