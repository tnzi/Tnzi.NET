
namespace Tnzi.EFCore.Services;

/// <summary>
/// DbContext 发现服务实现
/// </summary>
public class DbContextDiscoveryService : IDbContextDiscoveryService
{
    /// <summary>
    /// 发现并验证所有配置的 DbContext
    /// </summary>
    public DbContextDiscoveryResult DiscoverDbContexts(Options.DatabaseOptions options, ILogger? logger = null)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (options.DbContexts == null || options.DbContexts.Count == 0)
            {
                logger?.LogInformation("No database configurations found in 'Database' section. Skipping auto-registration.");
                return new DbContextDiscoveryResult
                {
                    ValidationErrors = new Dictionary<string, string>()
                };
            }

            logger?.LogDebug("Starting DbContext discovery for {Count} configuration(s).", options.DbContexts.Count);

            // 验证并解析配置
            var dbContextConfigs = new List<DbContextDiscoveryItem>();
            var nameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var validationErrors = new Dictionary<string, string>();

            foreach (var config in options.DbContexts)
            {
                var configSw = Stopwatch.StartNew();
                string? error = null;
                try
                {
                    var (item, itemError) = ValidateAndCreateItem(config, nameSet, logger);
                    error = itemError;
                    if (item != null)
                    {
                        dbContextConfigs.Add(item);
                        nameSet.Add(config.Name);
                        logger?.LogDebug(
                            "Validated DbContext configuration '{Name}' in {ElapsedMs}ms",
                            config.Name, configSw.ElapsedMilliseconds);
                    }
                    else if (error != null)
                    {
                        // 使用配置名称作为键，如果名称为空则使用索引
                        var key = string.IsNullOrEmpty(config.Name)
                            ? $"Index_{options.DbContexts.IndexOf(config)}"
                            : config.Name;
                        validationErrors[key] = error;
                        logger?.LogWarning(
                            "Failed to validate DbContext configuration '{Name}' after {ElapsedMs}ms: {Error}",
                            config.Name, configSw.ElapsedMilliseconds, error);
                    }
                }
                finally
                {
                    configSw.Stop();
                    if (configSw.ElapsedMilliseconds > 500) // 单个配置验证超过 500ms 记录警告
                    {
                        logger?.LogWarning(
                            "DbContext configuration '{Name}' validation took {ElapsedMs}ms, which is longer than expected. Consider explicitly specifying DbContextType and Provider.",
                            config.Name, configSw.ElapsedMilliseconds);
                    }
                }
            }

            // 确定主 DbContext（如果有有效配置）
            DbContextDiscoveryItem? primaryConfig = null;
            if (dbContextConfigs.Count > 0)
            {
                primaryConfig = DeterminePrimaryDbContext(dbContextConfigs, options.PrimaryDbContext, logger);
            }

            sw.Stop();
            logger?.LogInformation(
                "DbContext discovery completed in {ElapsedMs}ms. Found {ValidCount} valid configuration(s), {ErrorCount} error(s).",
                sw.ElapsedMilliseconds, dbContextConfigs.Count, validationErrors.Count);

            return new DbContextDiscoveryResult
            {
                Configurations = dbContextConfigs,
                PrimaryConfiguration = primaryConfig,
                ValidationErrors = validationErrors
            };
        }
        finally
        {
            sw.Stop();
            if (sw.ElapsedMilliseconds > 1000) // 如果发现过程耗时超过 1 秒，记录警告
            {
                logger?.LogWarning(
                    "DbContext discovery took {ElapsedMs}ms, which is longer than expected. Consider optimizing your configuration or explicitly specifying DbContextType and Provider.",
                    sw.ElapsedMilliseconds);
            }
        }
    }

    /// <summary>
    /// 验证配置并创建发现项
    /// </summary>
    /// <returns>返回 (发现项, 错误消息) 元组，如果验证失败则返回 (null, 错误消息)</returns>
    private (DbContextDiscoveryItem?, string?) ValidateAndCreateItem(
        Options.DbContextConfiguration config,
        HashSet<string> existingNames,
        ILogger? logger)
    {
        // 验证 Name
        if (string.IsNullOrEmpty(config.Name))
        {
            var error = "Name is empty";
            logger?.LogWarning("DbContext configuration has empty Name. Skipping.");
            return (null, error);
        }

        // 验证 Name 是否重复
        if (existingNames.Contains(config.Name))
        {
            var error = $"Duplicate Name '{config.Name}'";
            logger?.LogWarning("DbContext configuration with duplicate Name '{Name}'. Skipping.", config.Name);
            return (null, error);
        }

        // DbContextType 自动发现或验证
        Type? dbContextType = null;
        if (string.IsNullOrEmpty(config.DbContextType))
        {
            // 尝试自动发现 DbContextType
            try
            {
                dbContextType = config.AutoDiscoverDbContextType(logger);
                // 更新配置中的 DbContextType 以便后续使用
                config.DbContextType = dbContextType.FullName + ", " + dbContextType.Assembly.GetName().Name;
                logger?.LogInformation(
                    "Auto-discovered DbContextType '{DbContextType}' for Name '{Name}'.",
                    config.DbContextType, config.Name);
            }
            catch (InvalidOperationException ex)
            {
                var error = $"Cannot auto-discover DbContextType for Name '{config.Name}'. {ex.Message}";
                logger?.LogWarning("Failed to auto-discover DbContextType for Name '{Name}': {Error}",
                    config.Name, ex.Message);
                return (null, error);
            }
        }
        else
        {
            // DbContextType 已显式指定，使用现有解析逻辑
            dbContextType = config.GetDbContextType();
            if (dbContextType == null)
            {
                var error = $"Cannot resolve DbContext type '{config.DbContextType}'. Please ensure the type name is correct and the assembly is loaded";
                logger?.LogWarning("Cannot resolve DbContext type '{DbContextType}' for '{Name}'. Please ensure the type name is correct and the assembly is loaded.",
                    config.DbContextType, config.Name);
                return (null, error);
            }
        }

        // 验证 ConnectionString
        if (string.IsNullOrEmpty(config.ConnectionString))
        {
            var error = $"ConnectionString is empty";
            logger?.LogWarning("DbContext configuration for '{Name}' has empty ConnectionString. Skipping.", config.Name);
            return (null, error);
        }

        // Provider 自动识别或验证
        // 注意：由于枚举默认值就是 SqlServer (0)，我们无法区分"用户显式指定了 SqlServer"和"用户未指定（默认值）"
        // 为了保持向后兼容性，我们只在 Provider 为默认值且能够明确检测到不同值时，才进行自动识别
        // 如果检测失败或不明确，仍然使用默认值（SqlServer）
        if (config.Provider == DatabaseProvider.SqlServer && !string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            // 尝试自动识别，但只有在成功且明确的情况下才使用
            if (DatabaseProviderDetector.TryDetectFromConnectionString(config.ConnectionString, out var detectedProvider, out _, logger))
            {
                // 如果检测到的 Provider 与默认值不同，则使用检测到的值
                if (detectedProvider != DatabaseProvider.SqlServer)
                {
                    config.Provider = detectedProvider;
                    logger?.LogInformation(
                        "Auto-detected Provider '{Provider}' from connection string for DbContext '{Name}' (was default SqlServer).",
                        detectedProvider, config.Name);
                }
                // 如果检测到的也是 SqlServer，保持默认值（向后兼容）
            }
            // 如果检测失败，保持默认值 SqlServer（向后兼容，不抛出异常）
        }
        else
        {
            // Provider 已显式指定为其他值（非默认值），验证有效性并尊重用户选择
            if (!Enum.IsDefined(typeof(DatabaseProvider), config.Provider))
            {
                var error = $"Invalid Provider '{config.Provider}'";
                logger?.LogWarning("DbContext configuration for '{Name}' has invalid Provider '{Provider}'. Skipping.",
                    config.Name, config.Provider);
                return (null, error);
            }

            // 验证连接字符串与 Provider 的一致性（记录警告，不阻止启动）
            // 这有助于用户发现配置不一致的问题
            if (DatabaseProviderDetector.TryDetectFromConnectionString(config.ConnectionString, out var detectedProvider, out _, logger))
            {
                if (detectedProvider != config.Provider)
                {
                    logger?.LogWarning(
                        "Provider mismatch for DbContext '{Name}': configured '{ConfiguredProvider}' but connection string suggests '{DetectedProvider}'. " +
                        "Using configured Provider '{ConfiguredProvider}'. Please verify your configuration.",
                        config.Name, config.Provider, detectedProvider, config.Provider);
                }
            }
        }

        // 验证类型是否是 DbContext 的子类（自动发现的类型已经验证过，这里做防御性检查）
        if (dbContextType == null || !typeof(DbContext).IsAssignableFrom(dbContextType))
        {
            var error = dbContextType == null
                ? $"DbContext type is null for '{config.Name}'"
                : $"Type '{dbContextType.FullName}' is not a DbContext";
            logger?.LogWarning("Invalid DbContext type for '{Name}': {Error}",
                config.Name, error);
            return (null, error);
        }

        return (new DbContextDiscoveryItem
        {
            Configuration = config,
            DbContextType = dbContextType
        }, null);
    }

    /// <summary>
    /// 确定主 DbContext
    /// </summary>
    private DbContextDiscoveryItem DeterminePrimaryDbContext(
        List<DbContextDiscoveryItem> configs,
        string? primaryDbContextName,
        ILogger? logger)
    {
        if (!string.IsNullOrEmpty(primaryDbContextName))
        {
            var found = configs.FirstOrDefault(c =>
                string.Equals(c.Configuration.Name, primaryDbContextName, StringComparison.OrdinalIgnoreCase));

            if (found == null)
            {
                logger?.LogWarning(
                    "PrimaryDbContext '{PrimaryDbContext}' not found in DbContexts. Using the first one '{Name}' as primary.",
                    primaryDbContextName, configs[0].Configuration.Name);
                return configs[0];
            }

            logger?.LogInformation("Using '{Name}' as primary DbContext.", found.Configuration.Name);
            return found;
        }

        // 如果没有指定 PrimaryDbContext，使用第一个
        logger?.LogDebug("No PrimaryDbContext specified. Using the first one '{Name}' as primary.",
            configs[0].Configuration.Name);
        return configs[0];
    }
}