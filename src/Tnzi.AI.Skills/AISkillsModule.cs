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
        // AIModule 的 NoOp 回退在 PostConfigure 阶段才 TryAdd（见 AIModule.PostConfigureServicesAsync），
        // 本模块 Configure 阶段的注册必然先于回退 → 真实实现永远胜出，无须 RemoveAll。
        services.AddSingleton<ISkillTemplateEngine, SkillTemplateEngine>();
        services.AddSingleton<ISkillConstraintEnforcer, SkillConstraintEnforcer>();
        services.AddSingleton<ISkillRequirementsValidator, SkillRequirementsValidator>();
        services.AddScoped<ISkillSearchService, SkillSearchService>();
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
