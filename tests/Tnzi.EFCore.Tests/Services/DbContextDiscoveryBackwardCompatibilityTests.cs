
namespace Tnzi.EFCore.Tests.Services;

/// <summary>
/// DbContextDiscoveryService 向后兼容性测试
/// 验证现有配置文件仍然可以正常工作
/// </summary>
public class DbContextDiscoveryBackwardCompatibilityTests
{
    [Fact]
    public void DiscoverDbContexts_WithExplicitConfiguration_WorksAsBefore()
    {
        // Arrange - 模拟现有配置文件（显式指定所有值）
        var service = new DbContextDiscoveryService();
        var options = new DatabaseOptions
        {
            AutoDiscoverDbContexts = true,
            PrimaryDbContext = "Default",
            DbContexts = new List<DbContextConfiguration>
            {
                new DbContextConfiguration
                {
                    Name = "Default",
                    DbContextType = "Tnzi.EFCore.Tests.TestEntities.TestDbContext, Tnzi.EFCore.Tests",
                    ConnectionString = "Host=localhost;Port=5432;Database=test",
                    Provider = DatabaseProvider.PostgreSQL
                }
            }
        };

        // Act
        var result = service.DiscoverDbContexts(options);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Configurations);
        Assert.NotNull(result.PrimaryConfiguration);
        Assert.Equal("Default", result.PrimaryConfiguration!.Configuration.Name);
        Assert.Equal(DatabaseProvider.PostgreSQL, result.PrimaryConfiguration.Configuration.Provider);
        Assert.Equal(typeof(TestEntities.TestDbContext), result.PrimaryConfiguration.DbContextType);
    }

    [Fact]
    public void DiscoverDbContexts_WithNonDefaultProvider_NotAutoDetected()
    {
        // Arrange - 显式指定非默认 Provider（应该不触发自动识别）
        var service = new DbContextDiscoveryService();
        var options = new DatabaseOptions
        {
            AutoDiscoverDbContexts = true,
            DbContexts = new List<DbContextConfiguration>
            {
                new DbContextConfiguration
                {
                    Name = "Default",
                    DbContextType = "Tnzi.EFCore.Tests.TestEntities.TestDbContext, Tnzi.EFCore.Tests",
                    ConnectionString = "Server=localhost;Initial Catalog=test;User Id=sa;Password=password", // SQL Server 格式
                    Provider = DatabaseProvider.PostgreSQL // 但显式指定为 PostgreSQL
                }
            }
        };

        // Act
        var result = service.DiscoverDbContexts(options);

        // Assert
        // 应该使用显式指定的 Provider（非默认值），不会自动识别
        // 但会记录警告（Provider 与连接字符串不匹配）
        Assert.True(result.Success);
        Assert.Single(result.Configurations);
        Assert.Equal(DatabaseProvider.PostgreSQL, result.Configurations[0].Configuration.Provider);
    }

    [Fact]
    public void DiscoverDbContexts_WithExplicitDbContextType_NotAutoDiscovered()
    {
        // Arrange - 显式指定 DbContextType（应该不触发自动发现）
        var service = new DbContextDiscoveryService();
        var options = new DatabaseOptions
        {
            AutoDiscoverDbContexts = true,
            DbContexts = new List<DbContextConfiguration>
            {
                new DbContextConfiguration
                {
                    Name = "Test", // 如果自动发现，可能会匹配 TestDbContext
                    DbContextType = "Tnzi.EFCore.Tests.TestEntities.TestDbContext, Tnzi.EFCore.Tests", // 显式指定
                    ConnectionString = "Host=localhost;Port=5432;Database=test",
                    Provider = DatabaseProvider.PostgreSQL
                }
            }
        };

        // Act
        var result = service.DiscoverDbContexts(options);

        // Assert
        // 应该使用显式指定的 DbContextType，不会自动发现
        Assert.True(result.Success);
        Assert.Single(result.Configurations);
        Assert.Equal(typeof(TestEntities.TestDbContext), result.Configurations[0].DbContextType);
    }

    [Fact]
    public void DiscoverDbContexts_WithDefaultProvider_AutoDetects()
    {
        // Arrange - Provider 为默认值（SqlServer = 0），应该触发自动识别
        var service = new DbContextDiscoveryService();
        var options = new DatabaseOptions
        {
            AutoDiscoverDbContexts = true,
            DbContexts = new List<DbContextConfiguration>
            {
                new DbContextConfiguration
                {
                    Name = "Default",
                    DbContextType = "Tnzi.EFCore.Tests.TestEntities.TestDbContext, Tnzi.EFCore.Tests",
                    ConnectionString = "Host=localhost;Port=5432;Database=test", // PostgreSQL 格式
                    Provider = DatabaseProvider.SqlServer // 默认值，应该自动识别为 PostgreSQL
                }
            }
        };

        // Act
        var result = service.DiscoverDbContexts(options);

        // Assert
        // 应该自动识别为 PostgreSQL
        Assert.True(result.Success);
        Assert.Single(result.Configurations);
        // 注意：由于我们在配置中显式指定了 DbContextType，所以 Provider 会被自动识别
        // 但为了简化测试，我们验证配置是否成功
        Assert.Equal(DatabaseProvider.PostgreSQL, result.Configurations[0].Configuration.Provider);
    }
}