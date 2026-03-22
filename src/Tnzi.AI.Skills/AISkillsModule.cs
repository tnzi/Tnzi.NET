namespace Tnzi.AI.Skills;

/// <summary>
/// AI 技能管理模块 — 提供技能注册、搜索、模板引擎、约束执行等功能
/// </summary>
[DependsOn(typeof(AIModule))]
public class AISkillsModule : TnziCustomModule
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
        services.TryAddSingleton<ISkillTemplateEngine, SkillTemplateEngine>();
        services.TryAddSingleton<ISkillConstraintEnforcer, SkillConstraintEnforcer>();
        services.TryAddSingleton<ISkillRequirementsValidator, SkillRequirementsValidator>();
        services.TryAddScoped<ISkillSearchService, SkillSearchService>();
        services.AddScoped<DatabaseSkillStore>();
        services.TryAddScoped<ISkillRegistry, SkillRegistry>();
        services.AddScoped<ISkillService, SkillService>();

        // 注册技能约束中间件
        services.AddScoped<SkillConstraintMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<SkillConstraintMiddleware>());

        return Task.CompletedTask;
    }
}
