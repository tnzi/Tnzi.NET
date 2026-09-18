namespace Tnzi.EFCore.Internal;

/// <summary>
/// 宿主里「有哪些 DbContext」的唯一答案：<see cref="UnitOfWorkManager"/>（提交循环）与
/// <see cref="Services.EfCoreDbMigrator"/>（迁移循环）都读这里。
/// </summary>
/// <remarks>
/// <para>
/// 三条途径按可靠程度排序：<c>AddTnziDbContext</c> 的登记（<see cref="RegisteredDbContext"/>）&gt;
/// <c>Database:DbContexts</c> 配置里能解析出的 <c>DbContextType</c> &gt; <see cref="IEntityManager"/> 按实体配置反推。
/// ★ 后两条对最常见的形态都取不到主上下文：配置只写 <c>Name</c> 时读不到类型；
/// <c>IEntityManager.GetAllDbContextTypes()</c> 刻意排除承载「<c>DbContextType == null</c> 的实体」的 <c>object</c> 占位键，
/// 而实体配置的 <c>DbContextType</c> 默认就是 null。只靠后两条的调用方在主上下文上什么都不做而毫无症状：
/// 工作单元零次 SaveChanges、迁移器「No DbContext types found」。
/// </para>
/// <para>
/// 这份列表此前只长在 <c>UnitOfWorkManager</c> 里，迁移器自己另抄了一份只含第三条 —— 工作单元那侧补上登记时
/// 迁移器没跟上。两个调用方共用一处，漏改就不再可能。
/// </para>
/// <para>
/// ★ 失败方向关闭：一个都发现不了、而容器里<b>明明注册了</b> DbContext（裸 <c>AddDbContext</c> 绕过了漏斗）时抛异常，
/// 而不是记一条 Warning 后返回空列表 —— 空列表让每个调用方「成功地」什么都不做：提交循环零次 SaveChanges 而接口 200，
/// 迁移器「Skipping migration」。宿主里根本没有 DbContext（纯缓存 / 纯消息的轻宿主）不是错误，仍返回空列表由调用方记 Warning。
/// 判据来自 EF 自己的登记：<c>AddDbContext</c> 会把非泛型 <see cref="DbContextOptions"/> 逐个 <c>Add</c> 进容器，
/// <c>ContextType</c> 就是那个上下文。
/// </para>
/// </remarks>
internal static class DbContextTypeDiscovery
{
    public static List<Type> Discover(IServiceProvider serviceProvider, ILogger? logger)
    {
        Check.NotNull(serviceProvider);
        var dbContextTypes = CollectDbContextTypes(serviceProvider, logger);

        if (dbContextTypes.Count == 0)
        {
            ThrowIfContextsBypassedTheFunnel(serviceProvider);
        }

        return dbContextTypes;
    }

    private static void ThrowIfContextsBypassedTheFunnel(IServiceProvider serviceProvider)
    {
        var bypassed = serviceProvider.GetServices<DbContextOptions>()
            .Select(options => options.ContextType)
            .Distinct()
            .ToList();
        if (bypassed.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", bypassed.Select(type => type.Name));
        throw new InvalidOperationException(
            $"DbContext {names} is registered in the service container but not through AddTnziDbContext, " +
            "so the framework cannot discover it: changes made inside a unit of work would never be saved and " +
            "migrations would never be applied, while every call reported success. Register the DbContext with " +
            "services.AddTnziDbContext<TDbContext>(...) (or configure it under 'Database:DbContexts') instead of AddDbContext.");
    }

    private static List<Type> CollectDbContextTypes(IServiceProvider serviceProvider, ILogger? logger)
    {
        var dbContextTypes = new HashSet<Type>();

        foreach (var registered in serviceProvider.GetServices<RegisteredDbContext>())
        {
            dbContextTypes.Add(registered.DbContextType);
        }

        try
        {
            var configuration = serviceProvider.GetService<IConfiguration>();
            var databaseOptions = configuration?.GetSection("Database").Get<DatabaseOptions>();
            foreach (var dbContextConfig in databaseOptions?.DbContexts ?? [])
            {
                var dbContextType = dbContextConfig.GetDbContextType();
                if (dbContextType != null && typeof(DbContext).IsAssignableFrom(dbContextType))
                {
                    dbContextTypes.Add(dbContextType);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to read DbContext types from config");
        }

        try
        {
            var entityManager = serviceProvider.GetService<IEntityManager>();
            if (entityManager != null)
            {
                entityManager.Initialize();
                foreach (var dbContextType in entityManager.GetAllDbContextTypes())
                {
                    dbContextTypes.Add(dbContextType);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to get DbContext types from EntityManager");
        }

        return dbContextTypes.ToList();
    }
}
