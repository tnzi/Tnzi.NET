
using Microsoft.Extensions.Hosting;
using Tnzi.Data.Snows;

namespace Tnzi.Modules;

/// <summary>
/// Tnzi 核心服务模块
/// 注册核心服务（性能监控、常用的工具服务等）
/// </summary>
public class CoreServicesModule : TnziCoreModule
{
    /// <summary>
    /// 未配置机器码时非生产环境使用的默认值（与此前 <c>IdHelper.NextId</c> 的静默回退值相同，单机行为不变）。
    /// </summary>
    private const ushort DevelopmentDefaultWorkerId = 1;

    public override int LoadOrder => 0;

    /// <summary>
    /// 本次启动解析到的机器码；<c>null</c> = 未配置（用的是 <see cref="DevelopmentDefaultWorkerId"/>）。
    /// 在 Configure 阶段定下、在初始化阶段按环境决定失败方向（那时才有 logger 与宿主环境）。
    /// </summary>
    private WorkerIdResolution? _workerIdResolution;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddTnziOptions<IdGenerationOptions, IdGenerationOptionsValidator>(context.Configuration);

        // LLM 提供商注册表与轻量调用默认值 —— 绑定 AI 配置节的最小投影，
        // 使消费应用不加载 Tnzi.AI 模块也能用 IAiUtility（见 OpenAiCompatibleAiUtility）。
        context.Services.AddTnziOptions<AiProviderRegistryOptions, AiProviderRegistryOptionsValidator>(context.Configuration);
        context.Services.AddTnziOptions<AiUtilityOptions, AiUtilityOptionsValidator>(context.Configuration);

        // 提供商名称由配置字典的键回填 —— 配置文件里不必重复书写 Name。
        context.Services.PostConfigure<AiProviderRegistryOptions>(options =>
        {
            foreach (var (providerName, providerOptions) in options.Providers)
            {
                providerOptions.Name = providerName;
            }
        });

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册性能监控服务
        context.Services.TryAddSingleton<IPerformanceMonitorService, PerformanceMonitorService>();

        // 注册 TimeProvider（用于审计时间戳，支持可测试性）
        context.Services.TryAddSingleton(TimeProvider.System);

        ConfigureIdGeneration(context.Configuration);
        ConfigureAiUtility(context);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 按 <c>IdGeneration</c> 配置节初始化进程级雪花生成器。
    /// </summary>
    /// <remarks>
    /// ★ 放在 Configure 阶段而不是初始化阶段：<see cref="IdHelper"/> 是静态的，EF 的 SaveChanges、
    /// Payment 的单号生成都直接调 <c>IdHelper.NextId()</c>，必须在任何服务跑起来之前定下机器码。
    /// 此前全仓没有任何入口设置 WorkerId，多实例部署每个副本都静默用 1 —— 同一毫秒内产出相同的 long。
    /// 这里直接绑定配置节而不是解析 <c>IOptions</c>：还没有 ServiceProvider。
    /// 未配置时先按默认值初始化，让非生产环境零配置可跑；是否拦下由 <see cref="OnApplicationInitializationAsync"/>
    /// 拿到宿主环境后决定。
    /// </remarks>
    private void ConfigureIdGeneration(IConfiguration configuration)
    {
        var options = configuration.GetSection(ConfigSectionResolver.Resolve(typeof(IdGenerationOptions))).Get<IdGenerationOptions>()
            ?? new IdGenerationOptions();

        var validation = new IdGenerationOptionsValidator().Validate(null, options);
        if (validation.Failed)
        {
            throw new ConfigurationException("IdGeneration", $"Invalid IdGeneration configuration: {validation.FailureMessage}");
        }

        _workerIdResolution = WorkerIdResolver.Resolve(options, Environment.MachineName);

        IdHelper.SetIdGenerator(new IdGeneratorOptions
        {
            WorkerId = _workerIdResolution?.WorkerId ?? DevelopmentDefaultWorkerId,
            WorkerIdBitLength = options.WorkerIdBitLength,
            SeqBitLength = options.SeqBitLength,
        });
    }

    /// <summary>
    /// 机器码的失败方向：未配置时 Production 启动即失败并指名要配的键，其它环境记 Warning。
    /// </summary>
    /// <remarks>
    /// 环境判据是宿主的 <see cref="IHostEnvironment"/>（<c>ApplicationInitializationContext.Environment</c>
    /// 或 DI 里的注册），不是 <c>ServiceConfigurationContext.EnvironmentName</c>：后者对拿不到环境变量的
    /// 裸配置一律答「Production」，会把每一个用 ServiceCollection 装模块图的单元测试都拦死。
    /// 拿不到宿主环境 = 不是托管应用，答不出「是不是生产」⇒ 只警告不拦。真实部署总有 IHostEnvironment。
    /// </remarks>
    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var logger = context.ServiceProvider.GetService<ILogger<CoreServicesModule>>();

        if (_workerIdResolution != null)
        {
            logger?.LogInformation("Snowflake WorkerId {WorkerId} resolved from {Source}.",
                _workerIdResolution.WorkerId, _workerIdResolution.Source);
            return Task.CompletedTask;
        }

        IHostEnvironment? environment = context.Environment ?? context.ServiceProvider.GetService<IHostEnvironment>();
        if (environment?.IsProduction() == true)
        {
            throw new ConfigurationException(
                "IdGeneration:WorkerId",
                "Snowflake WorkerId is not configured. Every running instance must generate ids with a distinct " +
                "WorkerId, otherwise two instances produce identical long ids within the same millisecond " +
                "(primary-key conflicts, duplicated trade/invoice numbers). Set IdGeneration:WorkerId to a value " +
                "unique per instance (for example via the IdGeneration__WorkerId environment variable), or set " +
                "IdGeneration:WorkerIdFromHostname=true when the hostname carries a stable ordinal (StatefulSet pods).");
        }

        logger?.LogWarning(
            "Snowflake WorkerId is not configured; using the default WorkerId {WorkerId}. This is only safe for a " +
            "single instance. Set IdGeneration:WorkerId (or IdGeneration:WorkerIdFromHostname) before running " +
            "more than one instance; Production refuses to start without it.",
            DevelopmentDefaultWorkerId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 注册核心的轻量 AI 调用能力。
    /// </summary>
    /// <remarks>
    /// ★用 <c>TryAdd</c> 注册：本模块 LoadOrder 0，永远最先跑，所以这里的注册总会生效。
    /// 加载 <c>Tnzi.AI</c> 后由该模块**显式替换**本实现（它支持原生 Anthropic 协议、
    /// 数据库来源的提供商、降级链，核心版只走 OpenAI 兼容协议 + 配置来源），
    /// 替换方式见 <c>AIModule</c> 的注册代码 —— 那里不能用 TryAdd，会被本注册挡掉。
    /// <para>
    /// 未配置任何提供商时不会有任何副作用：HttpClient 是惰性创建的，
    /// <see cref="IAiUtility"/> 的调用会记一条 Warning 并返回 null。
    /// </para>
    /// </remarks>
    private static void ConfigureAiUtility(ServiceConfigurationContext context)
    {
        var services = context.Services;
        services.AddHttpClient();

        // 每个配置的提供商一个命名 HttpClient，使连接池按提供商隔离。
        foreach (var providerChild in context.Configuration.GetSection("AI:Providers").GetChildren())
        {
            var providerName = providerChild.Key;
            if (string.IsNullOrWhiteSpace(providerName))
            {
                continue;
            }

            if (!providerChild.GetValue("Enabled", defaultValue: true))
            {
                continue;
            }

            services.AddHttpClient(AiUtilityHttpClientNames.For(providerName));
        }

        // 运行时新增的提供商（不在配置清单里）共用的兜底客户端。
        services.AddHttpClient(AiUtilityHttpClientNames.Fallback);

        services.TryAddScoped<IAiUtility, OpenAiCompatibleAiUtility>();
    }
}
