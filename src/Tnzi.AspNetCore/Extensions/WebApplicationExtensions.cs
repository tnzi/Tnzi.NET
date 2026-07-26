
namespace Tnzi.AspNetCore.Extensions;

/// <summary>
/// WebApplication 扩展方法 - 简化 Tnzi.NET 框架初始化
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// 初始化 Tnzi.NET 框架（异步方式，支持 WebApplication）
    /// </summary>
    public static async Task UseTnziAsync(this WebApplication app)
    {
        var application = app.Services.GetRequiredService<ITnziApplication>();
        var env = app.Services.GetService<IWebHostEnvironment>();
        await application.InitializeAsync(app.Services, app, env, app);
    }

    /// <summary>
    /// 使用 Tnzi.NET 框架的默认配置初始化应用
    /// 包括：框架初始化、数据库迁移、种子数据初始化
    /// 注意：必须先初始化框架（包括 EntityManager），然后才能初始化数据库
    /// </summary>
    public static async Task UseTnziWithDefaultsAsync(this WebApplication app)
    {
        // 获取 Tnzi 选项
        var options = app.Services.GetService<IOptions<TnziOptions>>()?.Value ?? new TnziOptions();

        // 1. 首先初始化 Tnzi.NET 框架（包括 EntityManager 初始化）
        // 这一步必须在数据库初始化之前执行，因为 EntityManager 负责扫描和注册实体
        await app.UseTnziAsync();

        // 2. 然后初始化数据库迁移和种子数据
        // 此时 EntityManager 已初始化，能够正确发现所有实体
        await InitializeDatabasesAsync(app.Services, options);
    }

    /// <summary>
    /// Run every registered <see cref="IPostMigrationStartupTask"/> once, AFTER the
    /// migration phase, on every boot. Framework infrastructure that needs the schema
    /// to exist (e.g. syncing the code-declared permission catalogue + refreshing the
    /// in-memory snapshot) registers a task here instead of doing DB work in module
    /// init - which runs BEFORE migrations and fails on a brand-new empty database (the
    /// old "boot the app twice" bug). Errors are isolated: a failing task logs and
    /// startup continues. No-op when no tasks are registered.
    /// </summary>
    public static async Task RunPostMigrationStartupTasksAsync(this WebApplication app)
    {
        var tasks = app.Services.GetServices<IPostMigrationStartupTask>().ToList();
        if (tasks.Count == 0)
        {
            return;
        }

        var logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Tnzi.PostMigrationStartup");
        foreach (var task in tasks)
        {
            try
            {
                await task.ExecuteAsync(app.Services);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Post-migration startup task {TaskType} failed; startup continues.", task.GetType().Name);
            }
        }
    }

    /// <summary>
    /// 初始化所有数据库（迁移和种子数据）
    /// </summary>
    private static async Task InitializeDatabasesAsync(IServiceProvider serviceProvider, TnziOptions options)
    {
        var env = serviceProvider.GetService<IWebHostEnvironment>();
        var isProduction = env?.IsProduction() == true;

        // Orthogonal production gates: migrations (idempotent, safe) vs seeding (may mutate data).
        // Backward-compatible overlay on the legacy SkipDatabaseInitInProduction master switch -
        // when a new option is unset it falls back to the legacy switch, so existing appsettings
        // (default true → skip both; explicit false → run both) behave exactly as before.
        // Set ApplyMigrationsInProduction=true to migrate-without-seeding in production.
        // Non-production always runs both.
        var runMigrations = options.ShouldApplyMigrations(isProduction);
        var runSeed = options.ShouldSeed(isProduction);

        var rootLogger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Tnzi.DatabaseInitialization");

        if (isProduction && !runMigrations && !runSeed)
        {
            rootLogger.LogInformation(
                "Skipping database initialization in production environment (migrations and seeding both disabled).");
            return;
        }

        using var scope = serviceProvider.CreateScope();
        var loggerFactory2 = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger2 = loggerFactory2.CreateLogger("Tnzi.DatabaseInitialization");

        try
        {
            logger2.LogInformation("Starting database initialization...");

            // 获取所有数据库初始化器
            var dbInitializers = scope.ServiceProvider.GetServices<IDbInitializer>().ToList();

            if (dbInitializers.Count == 0)
            {
                logger2.LogDebug("No database initializers found, skipping database initialization.");
                return;
            }

            // 执行数据库迁移
            if (runMigrations)
            {
                logger2.LogInformation("Migrating {Count} database(s)...", dbInitializers.Count);
                foreach (var initializer in dbInitializers)
                {
                    try
                    {
                        await initializer.MigrateAsync();
                    }
                    catch (Exception ex)
                    {
                        logger2.LogError(ex, "Database migration failed for {InitializerType}", initializer.GetType().Name);

                        if (options.FailFastOnDatabaseInitError)
                        {
                            // 输出到控制台以便可见
                            Console.WriteLine($"[DB Init ERROR] {ex.GetType().Name}: {ex.Message}");
                            if (ex.InnerException != null)
                            {
                                Console.WriteLine($"[DB Init ERROR] Inner: {ex.InnerException.Message}");
                            }
                            throw;
                        }
                        else
                        {
                            // 不中断启动，只记录警告
                            logger2.LogWarning(ex, "Database migration failed but continuing startup (FailFastOnDatabaseInitError = false)");
                        }
                    }
                }

                logger2.LogInformation("Database migration completed.");
            }
            else
            {
                logger2.LogInformation(
                    "Skipping database migrations in production (ApplyMigrationsInProduction is not enabled).");
            }

            // 执行种子数据初始化
            if (runSeed)
            {
                var seeders = scope.ServiceProvider.GetServices<IDataSeeder>().ToList();
                if (seeders.Count > 0)
                {
                    logger2.LogDebug("Found {Count} data seeder(s), initializing seed data...", seeders.Count);
                    foreach (var seeder in seeders)
                    {
                        try
                        {
                            await seeder.SeedAsync(scope.ServiceProvider);
                        }
                        catch (Exception ex)
                        {
                            logger2.LogError(ex, "Seed data initialization failed for {SeederType}", seeder.GetType().Name);
                            // 种子数据失败不中断启动，只记录日志（除非 FailFastOnDatabaseInitError = true）
                            if (options.FailFastOnDatabaseInitError)
                            {
                                throw;
                            }
                        }
                    }
                    logger2.LogDebug("Seed data initialization completed.");
                }
            }
            else
            {
                logger2.LogInformation(
                    "Skipping seed data in production (SeedInProduction is not enabled).");
            }
        }
        catch (Exception ex)
        {
            logger2.LogError(ex, "Database initialization failed!");
            // 输出到控制台以便可见
            Console.WriteLine($"[DB Init ERROR] {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"[DB Init ERROR] Inner: {ex.InnerException.Message}");
            }

            // 根据配置决定是否抛出异常
            if (options.FailFastOnDatabaseInitError)
            {
                throw;
            }
            else
            {
                logger2.LogWarning("Database initialization failed but continuing startup (FailFastOnDatabaseInitError = false)");
            }
        }
    }
}
