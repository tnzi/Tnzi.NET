namespace Tnzi.EFCore.Internal;

/// <summary>
/// 数据库提供者检测器
/// 根据连接字符串格式自动识别数据库类型
/// </summary>
public static class DatabaseProviderDetector
{
    /// <summary>
    /// 从连接字符串检测数据库提供者
    /// </summary>
    /// <param name="connectionString">连接字符串</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <returns>检测到的数据库提供者</returns>
    /// <exception cref="InvalidOperationException">当连接字符串格式不明确或无法识别时抛出</exception>
    public static DatabaseProvider DetectFromConnectionString(string connectionString, ILogger? logger = null)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Connection string is empty. Cannot auto-detect database provider.");
            }

            if (TryDetectFromConnectionString(connectionString, out var provider, out var errorMessage, logger))
            {
                var elapsedMs = sw.ElapsedMilliseconds;
                sw.Stop();
                logger?.LogDebug(
                    "Auto-detected database provider '{Provider}' from connection string in {ElapsedMs}ms",
                    provider, elapsedMs);
                return provider;
            }

            throw new InvalidOperationException(errorMessage ?? "Cannot auto-detect database provider from connection string.");
        }
        finally
        {
            if (sw.IsRunning)
            {
                sw.Stop();
            }
            if (sw.ElapsedMilliseconds > 100) // 如果检测耗时超过 100ms，记录警告
            {
                logger?.LogWarning(
                    "Database provider detection took {ElapsedMs}ms, which is longer than expected. Consider explicitly specifying Provider in configuration.",
                    sw.ElapsedMilliseconds);
            }
        }
    }

    /// <summary>
    /// 尝试从连接字符串检测数据库提供者
    /// </summary>
    /// <param name="connectionString">连接字符串</param>
    /// <param name="provider">检测到的数据库提供者</param>
    /// <param name="errorMessage">错误消息（如果检测失败）</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <returns>如果成功检测返回 true，否则返回 false</returns>
    public static bool TryDetectFromConnectionString(string connectionString, out DatabaseProvider provider, out string? errorMessage, ILogger? logger = null)
    {
        provider = DatabaseProvider.SqlServer;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            errorMessage = "Connection string is empty. Cannot auto-detect database provider.";
            return false;
        }

        var connStr = connectionString.Trim();
        var matches = new HashSet<DatabaseProvider>(); // 使用 HashSet 优化 Contains 操作

        // 预先提取所有关键特征（避免重复检查，提升性能）
        var hasHost = ContainsKey(connStr, "Host=");
        var hasPort = ContainsKey(connStr, "Port=");
        var hasServer = ContainsKey(connStr, "Server=");
        var hasDataSource = ContainsKey(connStr, "Data Source=");
        var hasDbExtension = connStr.Contains(".db", StringComparison.OrdinalIgnoreCase);
        var hasInitialCatalog = ContainsKey(connStr, "Initial Catalog=");
        var hasDatabase = ContainsKey(connStr, "Database=");
        var hasUid = ContainsKey(connStr, "Uid=");
        var hasUser = ContainsKey(connStr, "User=");
        var hasUsername = ContainsKey(connStr, "Username="); // PostgreSQL 常用
        var hasUserId = ContainsKey(connStr, "User Id=");
        var hasIntegratedSecurity = ContainsKey(connStr, "Integrated Security=");

        // 按优先级检测（优先级高的先检测）

        // 优先级1: PostgreSQL - 识别特征：
        // - Host= 存在（Port= 可选，使用默认端口 5432）
        // - 包含 Database= 参数（PostgreSQL 常用）
        // - 通常使用 Username= 参数（PostgreSQL 特征，MySQL 使用 Uid=）
        // - 或者 Host= 和 Port= 同时存在（明确特征）
        // - 不包含 Initial Catalog=（SQL Server 特征）
        // - 不包含 Data Source=（SQL Server/SQLite 特征）
        if (hasHost && hasDatabase && !hasInitialCatalog && !hasDataSource)
        {
            // 进一步确认：如果包含 Username= 或同时有 Port=，肯定是 PostgreSQL
            // PostgreSQL 常用 Username=，MySQL 常用 Uid=
            var hasUserForPostgres = hasUser && !hasUid; // User= 但不是 Uid=，可能是 PostgreSQL

            if (hasUsername || hasPort || hasUserForPostgres)
            {
                matches.Add(DatabaseProvider.PostgreSQL);
                logger?.LogDebug("Matched PostgreSQL: Host={HasHost}, Database={HasDatabase}, Username={HasUsername}, Port={HasPort}, User={HasUser}",
                    hasHost, hasDatabase, hasUsername, hasPort, hasUserForPostgres);
            }
        }

        // 优先级2: SQLite - Data Source= 且包含 .db
        if (hasDataSource && hasDbExtension)
        {
            matches.Add(DatabaseProvider.Sqlite);
            logger?.LogDebug("Matched SQLite: Data Source with .db extension");
        }

        // 优先级3: MySQL - 识别特征：
        // - 使用 Server= 或 Host=（但不是 PostgreSQL，PostgreSQL 必须有 Port=）
        // - 包含 Uid= 或 User=（MySQL 特征，SQL Server 使用 User Id=）
        // - 不包含 Initial Catalog=（SQL Server 特征）
        // - 不包含 Data Source=（SQL Server/SQLite 特征）
        // - 如果没有匹配 PostgreSQL 或 SQLite，则可能是 MySQL
        // 注意：MySQL 也可能包含 Database= 参数，通过 Uid= 或 User= 来区分 SQL Server
        if (!matches.Contains(DatabaseProvider.PostgreSQL) &&
            !matches.Contains(DatabaseProvider.Sqlite))
        {
            var hasHostForMySql = hasHost && !hasPort; // Host= 但没有 Port=，不是 PostgreSQL
            var hasDataSourceForMySql = hasDataSource && !hasDbExtension; // Data Source= 但没有 .db，不是 SQLite

            // MySQL 使用 Uid= 或 User=，SQL Server 使用 User Id=
            if ((hasServer || hasHostForMySql) &&
                (hasUid || hasUser) &&
                !hasInitialCatalog &&
                !hasDataSourceForMySql)
            {
                matches.Add(DatabaseProvider.MySql);
                logger?.LogDebug(
                    "Matched MySQL: Server/Host={HasServerOrHost}, Uid/User={HasUidOrUser}, HasDatabase={HasDatabase}",
                    hasServer || hasHostForMySql, hasUid || hasUser, hasDatabase);
            }
        }

        // 优先级4: SQL Server - 识别特征（优化：去除冗余条件）
        // - Initial Catalog=（SQL Server 特有，最高优先级）
        // - Data Source=（且不包含 .db，已被 SQLite 检查排除）
        // - Database= 且包含 User Id= 或 Integrated Security=（SQL Server 特征）
        // 注意：如果已经匹配了 PostgreSQL、MySQL 或 SQLite，则不应该再匹配 SQL Server
        if (!matches.Contains(DatabaseProvider.PostgreSQL) &&
            !matches.Contains(DatabaseProvider.Sqlite) &&
            !matches.Contains(DatabaseProvider.MySql))
        {
            var hasDataSourceForSqlServer = hasDataSource && !hasDbExtension; // 复用前面检查的结果
            var hasDatabaseWithUser = hasDatabase && hasUserId;
            var hasDatabaseWithIntegratedSecurity = hasDatabase && hasIntegratedSecurity;

            if (hasInitialCatalog || hasDataSourceForSqlServer || hasDatabaseWithUser || hasDatabaseWithIntegratedSecurity)
            {
                matches.Add(DatabaseProvider.SqlServer);
                logger?.LogDebug(
                    "Matched SQL Server by: InitialCatalog={HasInitialCatalog}, DataSource={HasDataSource}, Database+User={HasDatabaseWithUser}, Database+IntegratedSecurity={HasDatabaseWithIntegratedSecurity}",
                    hasInitialCatalog, hasDataSourceForSqlServer, hasDatabaseWithUser, hasDatabaseWithIntegratedSecurity);
            }
        }

        // 处理匹配结果
        if (matches.Count == 0)
        {
            logger?.LogWarning(
                "Failed to detect database provider from connection string. No patterns matched. Connection string length: {Length}",
                connStr.Length);
            errorMessage = BuildErrorMessage(connStr, "Connection string format does not match any known database provider patterns.");
            return false;
        }

        if (matches.Count > 1)
        {
            logger?.LogWarning(
                "Ambiguous connection string format. Matched multiple providers: {Providers}. Connection string length: {Length}",
                string.Join(", ", matches), connStr.Length);
            errorMessage = BuildErrorMessage(connStr,
                $"Connection string format is ambiguous. Matched multiple providers: {string.Join(", ", matches)}");
            return false;
        }

        provider = matches.First(); // HashSet 使用 First() 获取唯一元素
        logger?.LogDebug("Successfully detected database provider: {Provider}", provider);
        return true;
    }

    /// <summary>
    /// 检查连接字符串是否包含指定的键（忽略大小写）
    /// </summary>
    private static bool ContainsKey(string connectionString, string key)
    {
        return connectionString.Contains(key, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 构建错误消息
    /// </summary>
    private static string BuildErrorMessage(string connectionString, string baseMessage)
    {
        return $"{baseMessage}\n\n" +
               "Possible solutions:\n" +
               "1. Explicitly specify the Provider in configuration:\n" +
               "   \"Provider\": \"PostgreSQL\" // or SqlServer, MySql, Sqlite\n\n" +
               "2. Use a more specific connection string format:\n" +
               "   PostgreSQL: Host=...;Port=...;Database=...\n" +
               "   SQL Server: Server=...;Initial Catalog=...;... (or Server=...;Database=...;User Id=...)\n" +
               "   MySQL: Server=...;Uid=...;Database=... (or Server=...;User=...;Database=...)\n" +
               "   SQLite: Data Source=...\n\n" +
               $"Connection string: {MaskSensitiveInfo(connectionString)}";
    }

    /// <summary>
    /// 屏蔽敏感信息（密码、密钥、令牌等）
    /// 修复：从后往前替换，避免多次替换导致位置偏移
    /// </summary>
    private static string MaskSensitiveInfo(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return connectionString;

        // 屏蔽敏感字段（密码、密钥、令牌等）
        var sensitiveKeys = new[]
        {
            "Password=", "Pwd=", "Pass=",
            "Secret=", "SecretKey=",
            "Token=", "AccessToken=", "RefreshToken=",
            "Key=", "ApiKey=", "PrivateKey=",
            "Certificate=", "Cert="
        };

        // 收集所有需要屏蔽的位置（从后往前排序，避免替换后位置偏移）
        var replacements = new List<(int startIndex, int endIndex, int keyLength)>();

        foreach (var key in sensitiveKeys)
        {
            int searchStart = 0;
            while (true)
            {
                var index = connectionString.IndexOf(key, searchStart, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    break;

                var startIndex = index + key.Length;
                var endIndex = connectionString.IndexOf(';', startIndex);
                if (endIndex < 0)
                    endIndex = connectionString.Length;

                replacements.Add((startIndex, endIndex, key.Length));
                searchStart = index + 1; // 继续搜索，支持多个相同键
            }
        }

        // 如果没有需要替换的内容，直接返回
        if (replacements.Count == 0)
            return connectionString;

        // 按位置从后往前排序，从后往前替换避免位置偏移
        replacements.Sort((a, b) => b.startIndex.CompareTo(a.startIndex));

        // 使用 StringBuilder 进行高效字符串构建
        var sb = new StringBuilder(connectionString);
        foreach (var (startIndex, endIndex, keyLength) in replacements)
        {
            var valueLength = endIndex - startIndex;
            var maskedLength = Math.Min(valueLength, 8);
            var maskedValue = new string('*', maskedLength);
            sb.Remove(startIndex, valueLength);
            sb.Insert(startIndex, maskedValue);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 从 DbContext 实例检测数据库提供者
    /// 统一的检测逻辑，供 EntityConfigurationContext 和 EntityTypeConfigurationBase 使用
    /// </summary>
    /// <param name="dbContext">数据库上下文（可为 null）</param>
    /// <returns>检测到的数据库提供者，如果无法确定则返回 null</returns>
    internal static DatabaseProvider? DetectFromDbContext(DbContext? dbContext)
    {
        if (dbContext == null)
            return null;

        // 方法1：优先从 DbContextOptions.Extensions 获取（在 Migration 和运行时都可用）
        try
        {
            var options = dbContext.GetService<IDbContextOptions>();
            if (options != null)
            {
                var extensions = options.Extensions;
                foreach (var extension in extensions)
                {
                    var extensionType = extension.GetType();
                    var extensionTypeName = extensionType.FullName ?? extensionType.Name;

                    if (extensionTypeName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
                        return DatabaseProvider.Sqlite;
                    if (extensionTypeName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
                        return DatabaseProvider.SqlServer;
                    if (extensionTypeName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase) ||
                        extensionTypeName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
                        return DatabaseProvider.PostgreSQL;
                    if (extensionTypeName.Contains("MySql", StringComparison.OrdinalIgnoreCase) ||
                        extensionTypeName.Contains("Pomelo", StringComparison.OrdinalIgnoreCase))
                        return DatabaseProvider.MySql;
                }
            }
        }
        catch
        {
            // 如果无法从 DbContextOptions 获取，尝试其他方法
        }

        // 方法2：尝试从 Database.ProviderName 获取（仅在运行时可用，Migration 时可能不可用）
        try
        {
            var providerName = dbContext.Database.ProviderName;
            if (!string.IsNullOrEmpty(providerName))
            {
                return ConvertProviderNameToDatabaseProvider(providerName);
            }
        }
        catch
        {
            // 在 OnModelCreating 阶段，Database.ProviderName 可能不可用（特别是 Migration 时）
        }

        return null;
    }

    /// <summary>
    /// 将 EF Core 提供者名称转换为 DatabaseProvider 枚举
    /// </summary>
    internal static DatabaseProvider? ConvertProviderNameToDatabaseProvider(string? providerName)
    {
        if (string.IsNullOrEmpty(providerName))
            return null;

        return providerName switch
        {
            "Microsoft.EntityFrameworkCore.SqlServer" => DatabaseProvider.SqlServer,
            "Npgsql.EntityFrameworkCore.PostgreSQL" => DatabaseProvider.PostgreSQL,
            "Pomelo.EntityFrameworkCore.MySql" or "MySql.EntityFrameworkCore" => DatabaseProvider.MySql,
            "Microsoft.EntityFrameworkCore.Sqlite" => DatabaseProvider.Sqlite,
            _ => null
        };
    }
}
