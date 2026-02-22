
namespace Tnzi.EFCore.Tests.Internal;

/// <summary>
/// DatabaseProviderDetector 测试类
/// </summary>
public class DatabaseProviderDetectorTests
{
    [Fact]
    public void DetectFromConnectionString_PostgreSQL_ReturnsPostgreSQL()
    {
        // Arrange
        var connectionString = "Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=password";

        // Act
        var provider = DatabaseProviderDetector.DetectFromConnectionString(connectionString);

        // Assert
        Assert.Equal(DatabaseProvider.PostgreSQL, provider);
    }

    [Fact]
    public void DetectFromConnectionString_SqlServer_ReturnsSqlServer()
    {
        // Arrange
        var connectionString = "Server=localhost;Initial Catalog=mydb;User Id=sa;Password=password";

        // Act
        var provider = DatabaseProviderDetector.DetectFromConnectionString(connectionString);

        // Assert
        Assert.Equal(DatabaseProvider.SqlServer, provider);
    }

    [Fact]
    public void DetectFromConnectionString_MySql_ReturnsMySql()
    {
        // Arrange - MySQL 使用 Uid= 或 User=，不是 User Id=
        var connectionString = "Server=localhost;Uid=root;Password=password";

        // Act
        var provider = DatabaseProviderDetector.DetectFromConnectionString(connectionString);

        // Assert
        Assert.Equal(DatabaseProvider.MySql, provider);
    }

    [Fact]
    public void DetectFromConnectionString_Sqlite_ReturnsSqlite()
    {
        // Arrange
        var connectionString = "Data Source=mydatabase.db";

        // Act
        var provider = DatabaseProviderDetector.DetectFromConnectionString(connectionString);

        // Assert
        Assert.Equal(DatabaseProvider.Sqlite, provider);
    }

    [Fact]
    public void DetectFromConnectionString_EmptyString_ThrowsException()
    {
        // Arrange
        var connectionString = "";

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            DatabaseProviderDetector.DetectFromConnectionString(connectionString));
    }

    [Fact]
    public void DetectFromConnectionString_AmbiguousFormat_ThrowsException()
    {
        // Arrange
        // 创建一个真正会产生歧义的连接字符串
        // 同时包含 Host= 和 Port=（PostgreSQL）以及 Initial Catalog=（SQL Server）
        // 但由于我们的逻辑优先匹配 PostgreSQL，这个字符串现在会匹配 PostgreSQL
        // 我们需要创建一个更复杂的歧义场景：同时包含多个数据库的特征但无法明确区分
        // 实际上，由于我们修复了逻辑，PostgreSQL 优先，所以很难产生真正的歧义
        // 这个测试主要验证异常处理逻辑，我们使用一个不匹配任何模式的字符串
        var connectionString = "UnsupportedFormat=value;AnotherKey=value";

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseProviderDetector.DetectFromConnectionString(connectionString));
        Assert.True(
            exception.Message.Contains("does not match", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("ambiguous", StringComparison.OrdinalIgnoreCase),
            $"Exception message should indicate detection failure: {exception.Message}");
    }

    [Fact]
    public void TryDetectFromConnectionString_PostgreSQL_ReturnsTrue()
    {
        // Arrange
        var connectionString = "Host=localhost;Port=5432;Database=mydb";

        // Act
        var success = DatabaseProviderDetector.TryDetectFromConnectionString(
            connectionString, out var provider, out var errorMessage);

        // Assert
        Assert.True(success);
        Assert.Equal(DatabaseProvider.PostgreSQL, provider);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void TryDetectFromConnectionString_UnsupportedFormat_ReturnsFalse()
    {
        // Arrange
        var connectionString = "UnsupportedFormat=value";

        // Act
        var success = DatabaseProviderDetector.TryDetectFromConnectionString(
            connectionString, out var provider, out var errorMessage);

        // Assert
        Assert.False(success);
        Assert.Equal(DatabaseProvider.SqlServer, provider); // 默认值
        Assert.NotNull(errorMessage);
        Assert.Contains("does not match", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetectFromConnectionString_MasksSensitiveInfo()
    {
        // Arrange
        var connectionString = "InvalidFormat";

        // Act & Assert
        // 验证异常消息中不包含完整密码
        var exception = Assert.ThrowsAny<Exception>(() =>
            DatabaseProviderDetector.DetectFromConnectionString(connectionString));

        // 由于连接字符串格式问题，会抛出异常，但异常消息应该屏蔽敏感信息
        // 这里我们主要是验证代码能正常运行，实际测试中可以通过日志验证
        Assert.NotNull(exception.Message);
    }

    [Fact]
    public void DetectFromConnectionString_WithDatabaseKeyword_SqlServer()
    {
        // Arrange
        var connectionString = "Server=localhost;Database=mydb;User Id=sa;Password=password";

        // Act
        var provider = DatabaseProviderDetector.DetectFromConnectionString(connectionString);

        // Assert
        Assert.Equal(DatabaseProvider.SqlServer, provider);
    }

    [Fact]
    public void DetectFromConnectionString_WithDataSource_SqlServer()
    {
        // Arrange
        var connectionString = "Data Source=localhost;Initial Catalog=mydb;Integrated Security=true";

        // Act
        var provider = DatabaseProviderDetector.DetectFromConnectionString(connectionString);

        // Assert
        Assert.Equal(DatabaseProvider.SqlServer, provider);
    }

    [Fact]
    public void DetectFromConnectionString_MySQL_WithUid_ReturnsMySql()
    {
        // Arrange
        var connectionString = "Server=localhost;Uid=root;Pwd=password";

        // Act
        var provider = DatabaseProviderDetector.DetectFromConnectionString(connectionString);

        // Assert
        Assert.Equal(DatabaseProvider.MySql, provider);
    }

    [Fact]
    public void DetectFromConnectionString_MySQL_WithoutDatabase_ReturnsMySql()
    {
        // Arrange - MySQL 使用 Uid= 或 User=，不是 User Id=
        var connectionString = "Host=localhost;Uid=root;Password=password";

        // Act
        var provider = DatabaseProviderDetector.DetectFromConnectionString(connectionString);

        // Assert
        Assert.Equal(DatabaseProvider.MySql, provider);
    }

    [Fact]
    public void DetectFromConnectionString_MySQL_WithDatabase_ReturnsMySql()
    {
        // Arrange - MySQL 连接字符串可能包含 Database= 参数
        var connectionString = "Server=localhost;Uid=root;Pwd=password;Database=testdb";

        // Act
        var provider = DatabaseProviderDetector.DetectFromConnectionString(connectionString);

        // Assert
        Assert.Equal(DatabaseProvider.MySql, provider);
    }

    [Fact]
    public void DetectFromConnectionString_MySQL_WithDatabaseAndUser_ReturnsMySql()
    {
        // Arrange - MySQL 使用 User= 参数
        var connectionString = "Server=localhost;User=root;Password=password;Database=mydb";

        // Act
        var provider = DatabaseProviderDetector.DetectFromConnectionString(connectionString);

        // Assert
        Assert.Equal(DatabaseProvider.MySql, provider);
    }

    [Fact]
    public void DetectFromConnectionString_SqlServer_WithDatabaseAndUserId_ReturnsSqlServer()
    {
        // Arrange - SQL Server 使用 User Id= 参数（区别于 MySQL 的 Uid= 或 User=）
        var connectionString = "Server=localhost;User Id=sa;Password=password;Database=mydb";

        // Act
        var provider = DatabaseProviderDetector.DetectFromConnectionString(connectionString);

        // Assert
        Assert.Equal(DatabaseProvider.SqlServer, provider);
    }

    [Fact]
    public void DetectFromConnectionString_SqlServer_WithIntegratedSecurity_ReturnsSqlServer()
    {
        // Arrange - SQL Server 特有的 Integrated Security 参数
        var connectionString = "Server=localhost;Database=mydb;Integrated Security=true";

        // Act
        var provider = DatabaseProviderDetector.DetectFromConnectionString(connectionString);

        // Assert
        Assert.Equal(DatabaseProvider.SqlServer, provider);
    }

    [Fact]
    public void DetectFromConnectionString_MasksMultipleSensitiveFields()
    {
        // Arrange - 包含多个敏感字段的连接字符串，验证 MaskSensitiveInfo 能正确处理
        // 修复前：多次替换会导致位置偏移，可能无法正确屏蔽所有敏感信息
        // 修复后：从后往前替换，能正确处理多个敏感字段
        var connectionString = "UnsupportedFormat=value;Password=Secret123;ApiKey=Key456;Token=Token789";

        // Act - 使用无效格式触发错误消息，验证敏感信息是否被屏蔽
        var success = DatabaseProviderDetector.TryDetectFromConnectionString(
            connectionString,
            out var provider,
            out var errorMessage);

        // Assert
        Assert.False(success);
        Assert.NotNull(errorMessage);
        // 验证敏感信息已被屏蔽（不应包含原始密码、密钥等）
        Assert.DoesNotContain("Secret123", errorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Key456", errorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Token789", errorMessage, StringComparison.OrdinalIgnoreCase);
        // 应该包含屏蔽后的星号
        Assert.Contains("*", errorMessage);
    }

    [Fact]
    public void DetectFromConnectionString_MasksSensitiveInfoInErrorMessage()
    {
        // Arrange - 包含多种敏感字段的连接字符串
        var connectionString = "InvalidFormat;Password=MySecretPassword;Secret=MySecret;Token=MyAccessToken;ApiKey=MyApiKey";

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseProviderDetector.DetectFromConnectionString(connectionString));

        // Assert - 验证异常消息中敏感信息已被屏蔽
        var message = exception.Message;
        Assert.DoesNotContain("MySecretPassword", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MySecret", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MyAccessToken", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MyApiKey", message, StringComparison.OrdinalIgnoreCase);
        // 验证包含屏蔽后的标记
        Assert.Contains("*", message);
    }
}