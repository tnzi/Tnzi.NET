namespace Tnzi.AspNetCore.Startup;

/// <summary>
/// 迁移后启动任务的运行器、后台重试与健康检查 <c>startup-tasks</c> 的注册。
/// </summary>
internal static class PostMigrationStartupTaskServiceCollectionExtensions
{
    internal static IServiceCollection AddPostMigrationStartupTaskRetry(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTnziOptions<PostMigrationStartupTaskOptions, PostMigrationStartupTaskOptionsValidator>(configuration);
        services.TryAddSingleton<PostMigrationStartupTaskRunner>();
        services.AddHostedService<PostMigrationStartupTaskRetryService>();
        services.AddHealthChecks().AddCheck<PostMigrationStartupTaskHealthCheck>(
            "startup-tasks",
            tags: ["startup", "database"]);
        return services;
    }
}
