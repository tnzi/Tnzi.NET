using McpClientFactory = Tnzi.AI.Infrastructure.Mcp.McpClientFactory;

namespace Tnzi.AI;

/// <summary>
/// AI 模块 - 基于 Microsoft.Extensions.AI 的自定义 Agent 引擎
/// </summary>
[DependsOn(typeof(EFCoreModule))]
[DependsOn(typeof(AspNetCoreModule))]
public partial class AIModule : TnziApplicationModule
{
    /// <summary>
    /// AI 模块加载顺序
    /// </summary>
    public override int LoadOrder => 50;

    /// <summary>
    /// 表名前缀
    /// </summary>
    public override string? TableNamePrefix => "AI";

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 绑定配置选项
        BindAndValidate<AIOptions, AIOptionsValidator>(context, "AI");
        BindAndValidate<AiUtilityOptions, AiUtilityOptionsValidator>(context, "AI:Utility");
        BindAndValidate<ThreadOptions, ThreadOptionsValidator>(context, "AI:Thread");
        BindAndValidate<LoopDetectionOptions, LoopDetectionOptionsValidator>(context, "AI:LoopDetection");
        BindAndValidate<SubAgentOptions, SubAgentOptionsValidator>(context, "AI:SubAgent");
        BindAndValidate<BudgetOptions, BudgetOptionsValidator>(context, "AI:Budget");
        BindAndValidate<SuggestionOptions, SuggestionOptionsValidator>(context, "AI:Suggestions");
        BindAndValidate<TodoOptions, TodoOptionsValidator>(context, "AI:Todo");
        BindAndValidate<Tools.Sql.SqlToolOptions, Tools.Sql.SqlToolOptionsValidator>(context, "AI:Sql");

        // 从环境变量补充 API Key，并回填 Provider Name（用于 Polly pipeline 路由）
        context.Services.PostConfigure<AIOptions>(options =>
        {
            foreach (var (providerName, providerOptions) in options.Providers)
            {
                // 回填 Name — configuration dictionary key 是权威来源
                providerOptions.Name = providerName;

                if (string.IsNullOrWhiteSpace(providerOptions.ApiKey))
                {
                    var envVarName = $"AI__{providerName.ToUpperInvariant()}__APIKEY";
                    var envKey = Environment.GetEnvironmentVariable(envVarName);
                    if (!string.IsNullOrWhiteSpace(envKey))
                    {
                        providerOptions.ApiKey = envKey;
                    }
                }
            }
        });

        return Task.CompletedTask;
    }

    private static void BindAndValidate<TOptions, TValidator>(
        ServiceConfigurationContext context, string section)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        context.Services.AddOptions<TOptions>()
            .Bind(context.Configuration.GetSection(section))
            .ValidateWith<TOptions, TValidator>();
    }

    private static void AddAiMiddleware<T>(IServiceCollection services)
        where T : class, IAiMiddleware
    {
        services.AddScoped<T>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<T>());
    }

    private static void AddAiMiddlewareSingleton<T>(IServiceCollection services,
        bool forwardAsSingleton = false)
        where T : class, IAiMiddleware
    {
        services.AddSingleton<T>();
        if (forwardAsSingleton)
            services.AddSingleton<IAiMiddleware>(sp => sp.GetRequiredService<T>());
        else
            services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<T>());
    }

    private static void AddToolMiddleware<T>(IServiceCollection services)
        where T : class, IAiMiddleware, IToolExecutionMiddleware
    {
        services.AddScoped<T>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<T>());
        services.AddScoped<IToolExecutionMiddleware>(sp => sp.GetRequiredService<T>());
    }

    private static void AddInputGuardrail<T>(IServiceCollection services)
        where T : class, IInputGuardrail, IGuardrailProvider
    {
        services.AddScoped<T>();
        services.AddScoped<IInputGuardrail>(sp => sp.GetRequiredService<T>());
        services.AddScoped<IGuardrailProvider>(sp => sp.GetRequiredService<T>());
    }

    private static void AddOutputGuardrail<T>(IServiceCollection services)
        where T : class, IOutputGuardrail, IGuardrailProvider
    {
        services.AddScoped<T>();
        services.AddScoped<IOutputGuardrail>(sp => sp.GetRequiredService<T>());
        services.AddScoped<IGuardrailProvider>(sp => sp.GetRequiredService<T>());
    }

    private static void AddDualGuardrail<T>(IServiceCollection services)
        where T : class, IInputGuardrail, IOutputGuardrail, IGuardrailProvider
    {
        services.AddScoped<T>();
        services.AddScoped<IInputGuardrail>(sp => sp.GetRequiredService<T>());
        services.AddScoped<IOutputGuardrail>(sp => sp.GetRequiredService<T>());
        services.AddScoped<IGuardrailProvider>(sp => sp.GetRequiredService<T>());
    }

    private static void AddProviderOnlyGuardrail<T>(IServiceCollection services)
        where T : class, IGuardrailProvider
    {
        services.AddScoped<T>();
        services.AddScoped<IGuardrailProvider>(sp => sp.GetRequiredService<T>());
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // 注册分组按关注点拆分到 AIModule.Registration.cs（partial）。
        // ⚠️ 调用顺序必须保持：TryAdd「先注册者胜」与 Add 覆盖语义依赖于此顺序不变。
        ConfigureHttpClients(context, services);
        RegisterChatClientsAndProcessors(services);
        RegisterToolAndAgentInfrastructure(services);
        RegisterOptionalSubmoduleFallbacks(services);
        RegisterConversationMemoryAndContext(services);
        RegisterCoreServices(services);
        RegisterBuiltInToolsAndGuardrails(services);
        RegisterMiddlewarePipeline(services);
        RegisterRuntimeAndRunServices(services);
        RegisterUtilitiesWorkspaceAndEvents(context, services);
        RegisterSqlToolSuite(services);

        return Task.CompletedTask;
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var serviceProvider = context.ServiceProvider;
        var logger = serviceProvider.GetRequiredService<ILogger<AIModule>>();
        ValidateRuntimeConfiguration(serviceProvider, logger);

        // 扫描并注册工具（在应用初始化阶段执行一次）
        var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();
        var toolScanner = serviceProvider.GetRequiredService<IToolScanner>();

        // 扫描自身程序集
        var assembly = Assembly.GetExecutingAssembly();
        RegisterTools(toolRegistry, toolScanner, assembly, logger);

        // 扫描应用程序集（排除框架程序集和系统程序集）
        var appAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic
                && a.FullName != null
                && !a.FullName.StartsWith("System.", StringComparison.Ordinal)
                && !a.FullName.StartsWith("Microsoft.", StringComparison.Ordinal)
                && !IsFrameworkCoreAssembly(a)
                && a != assembly);

        foreach (var appAssembly in appAssemblies)
        {
            RegisterTools(toolRegistry, toolScanner, appAssembly, logger);
        }

        // 校验 [RequiresSkill] 引用的 slug 是否存在
        await ValidateRequiresSkillReferencesAsync(toolRegistry, serviceProvider, logger);

        // Auto-disable framework built-in tools when an application assembly registers the same tool group.
        // This prevents duplicate tools when an app ships its own MemoryTools (or similar) in the "memory" group.
        var frameworkBuiltInTypes = new[] { typeof(DateTimeTools), typeof(TextTools), typeof(WebSearchTools), typeof(MemoryTools) };
        foreach (var builtInType in frameworkBuiltInTypes)
        {
            var builtInGroupAttr = builtInType.GetCustomAttribute<AIToolGroupAttribute>();
            if (builtInGroupAttr == null) continue;

            var groupName = builtInGroupAttr.GroupName;
            var toolsInGroup = toolRegistry.GetToolsByGroup(groupName);

            // Check if any tool in this group comes from a non-framework provider type
            var hasAppProvider = toolsInGroup.Any(t =>
                t.ProviderType != builtInType
                && !IsFrameworkCoreAssembly(t.ProviderType.Assembly));

            if (hasAppProvider)
            {
                toolRegistry.UnregisterByProviderType(builtInType);
                logger.LogInformation(
                    "Auto-disabled built-in {ToolType} because application registered a provider in the same tool group '{Group}'",
                    builtInType.Name, groupName);
            }
        }

        // 根据 BuiltInToolsOptions 按 ProviderType 精确移除已禁用的内置工具
        // 使用 UnregisterByProviderType 而非 UnregisterGroup，避免误删用户注册的同名工具组
        var aiOptions = serviceProvider.GetRequiredService<IOptions<AIOptions>>().Value;
        var builtInOptions = aiOptions.BuiltInTools;
        if (!builtInOptions.Enabled)
        {
            toolRegistry.UnregisterByProviderType(typeof(DateTimeTools));
            toolRegistry.UnregisterByProviderType(typeof(TextTools));
            toolRegistry.UnregisterByProviderType(typeof(WebSearchTools));
            toolRegistry.UnregisterByProviderType(typeof(MemoryTools));
        }
        else
        {
            if (!builtInOptions.EnableDateTime) toolRegistry.UnregisterByProviderType(typeof(DateTimeTools));
            if (!builtInOptions.EnableText) toolRegistry.UnregisterByProviderType(typeof(TextTools));
            if (!builtInOptions.EnableWebSearch) toolRegistry.UnregisterByProviderType(typeof(WebSearchTools));
            if (!builtInOptions.EnableMemory) toolRegistry.UnregisterByProviderType(typeof(MemoryTools));
        }

        // 从数据库加载持久化的子 Agent 类型定义
        try
        {
            var subAgentRegistry = serviceProvider.GetRequiredService<ISubAgentRegistry>();
            using var subAgentScope = serviceProvider.CreateScope();
            var subAgentTypeRepo = subAgentScope.ServiceProvider.GetRequiredService<IRepository<SubAgentType, Guid>>();
            await subAgentRegistry.LoadFromStoreAsync(subAgentTypeRepo);
            logger.LogDebug("Sub-agent type definitions loaded from database.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load sub-agent type definitions from database. Using built-in types only.");
        }

        // Memory 工具依赖 ContextProviders 的记忆召回能力 —— 如果召回不可用，
        // 注册 save_memory 工具只会产生永远不会被想起的记忆，误导 AI 和用户。
        // 移除整个 "memory" 工具组（包含框架内置和应用自定义的 memory 工具）。
        var contextProviders = aiOptions.ContextProviders;
        if (!contextProviders.Enabled || !contextProviders.Memory.Enabled)
        {
            toolRegistry.UnregisterGroup("memory");
            logger.LogInformation(
                "Memory tool group disabled: ContextProviders.Enabled={ContextEnabled}, Memory.Enabled={MemoryEnabled}. " +
                "Enable both to allow AI to save and recall memories",
                contextProviders.Enabled, contextProviders.Memory.Enabled);
        }
    }

    // Validation and tool-registration helpers live in AIModule.Validation.cs
    // (partial class) to keep this file focused on module wiring.
    // Service registration is split by concern into AIModule.Registration.cs (partial).
}
