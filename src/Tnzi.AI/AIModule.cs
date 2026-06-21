namespace Tnzi.AI;

/// <summary>
/// AI 模块 — 开箱即用的 AI 集成：契约、实体、DTO、Options，以及 Agent 执行引擎
/// （Runtime/Executor/Resolver/Factory、中间件管道、内置工具、Guardrails、Agent/Chat/Run 服务、控制器）。
/// <para>
/// 可选子模块（Workflow/Skills/Rag 等）的接口在本模块定义并带 NoOp 回退
/// （在 <see cref="PostConfigureServicesAsync"/> 注册，子模块 Configure 期注册必胜）；
/// 加载相应子模块获得真实实现。应用 <c>[DependsOn(typeof(AIModule))]</c> 即获得完整 AI 能力。
/// </para>
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
        context.Services.AddTnziOptions<TOptions, TValidator>(context.Configuration, section);
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // 引擎服务注册 — 拆分到 AIModule.Registration.cs（partial）。
        // ⚠️ 调用顺序必须保持：TryAdd「先注册者胜」语义依赖于此顺序不变。
        ConfigureHttpClients(context, services);
        RegisterChatClientsAndProcessors(services);
        RegisterToolAndAgentInfrastructure(services);
        RegisterConversationMemoryAndContext(services);
        RegisterCoreServices(services);
        RegisterBuiltInToolsAndGuardrails(services);
        RegisterMiddlewarePipeline(services);
        RegisterRuntimeAndRunServices(services);
        RegisterUtilitiesWorkspaceAndEvents(context, services);
        RegisterSqlToolSuite(services);

        // 核心工具：Shell 命令分析器 — 纯字符串解析工具，被 Sandbox 子模块消费
        services.TryAddSingleton<IShellCommandAnalyzer, ShellCommandAnalyzer>();

        return Task.CompletedTask;
    }

    public override Task PostConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 可选子模块 NoOp 回退注册在 PostConfigure 阶段（框架生命周期：所有模块 Configure 之后才进入
        // PostConfigure）。任何子模块/应用在 Configure 阶段的注册——无论 TryAdd 还是 Add——都先于这里的
        // TryAdd 回退，回退见「已存在」即跳过 → 结构性消灭「先注册的 NoOp 抢占子模块 TryAdd」整类
        // bug（ced3e778），子模块不需要 RemoveAll+Add 约定，用任意常规注册方式即可。
        RegisterOptionalSubmoduleFallbacks(context.Services);

        return Task.CompletedTask;
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var serviceProvider = context.ServiceProvider;
        var logger = serviceProvider.GetRequiredService<ILogger<AIModule>>();

        // 探测可选子模块的 NoOp 回退是否仍生效（信息级日志 + ITextSearch 有条件硬错误）。
        ValidateNoOpFallbacks(serviceProvider, logger);

        // 校验引擎运行时配置（启用了需要 LLM Provider 的功能时必须存在已启用的 Provider）
        ValidateProviderRuntimeConfiguration(serviceProvider, logger);

        // 扫描并注册工具（在应用初始化阶段执行一次）
        var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();
        var toolScanner = serviceProvider.GetRequiredService<IToolScanner>();

        // 扫描自身程序集（共享助手 AIToolRegistration — 与子模块同一条扫描-注册-容错路径）
        var assembly = Assembly.GetExecutingAssembly();
        AIToolRegistration.ScanAndRegisterAITools(toolRegistry, toolScanner, assembly, logger);

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
            AIToolRegistration.ScanAndRegisterAITools(toolRegistry, toolScanner, appAssembly, logger);
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

    // Service registration is split into AIModule.Registration.cs (partial):
    //   - engine registrations (ConfigureServicesAsync orchestration targets)
    //   - optional sub-module NoOp fallbacks (PostConfigureServicesAsync)
    // Validation/probing helpers live in AIModule.Validation.cs (partial).
}
