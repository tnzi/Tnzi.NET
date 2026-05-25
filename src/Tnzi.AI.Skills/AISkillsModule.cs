namespace Tnzi.AI.Skills;

/// <summary>
/// AI 技能管理模块 — 提供技能注册、搜索、模板引擎、约束执行等功能
/// </summary>
[DependsOn(typeof(AIModule))]
public class AISkillsModule : TnziApplicationModule
{
    /// <summary>
    /// 加载顺序（在 AIModule(50) 之后）
    /// </summary>
    public override int LoadOrder => 51;

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // 注册技能系统
        services.AddScoped<ISkillLoadTracker, SkillLoadTracker>();
        services.AddSingleton<FileSystemSkillStore>();
        // Expose FileSystemSkillStore as the default ISkillStore so that
        // singleton consumers (SandboxModule startup, ThreadDataMiddleware
        // optional injection) can resolve it. DatabaseSkillStore is scoped
        // and not suitable as a singleton default.
        services.AddSingleton<ISkillStore>(sp => sp.GetRequiredService<FileSystemSkillStore>());
        services.TryAddSingleton<ISkillTemplateEngine, SkillTemplateEngine>();
        services.TryAddSingleton<ISkillConstraintEnforcer, SkillConstraintEnforcer>();
        services.TryAddSingleton<ISkillRequirementsValidator, SkillRequirementsValidator>();
        services.TryAddScoped<ISkillSearchService, SkillSearchService>();
        services.AddScoped<DatabaseSkillStore>();
        services.TryAddScoped<ISkillRegistry, SkillRegistry>();
        services.AddScoped<ISkillService, SkillService>();
        services.AddScoped<ISkillCategoryService, SkillCategoryService>();

        // 注册事件处理器
        services.AddEventHandler<SkillActivatedEvent, SkillActivatedEventHandler>();

        // 注册技能约束中间件
        services.AddScoped<SkillConstraintMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<SkillConstraintMiddleware>());

        return Task.CompletedTask;
    }
}
