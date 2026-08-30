
namespace Tnzi.Modules;

/// <summary>
/// Tnzi 核心服务模块
/// 注册核心服务（性能监控、常用的工具服务等）
/// </summary>
public class CoreServicesModule : TnziCoreModule
{
    public override int LoadOrder => 0;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
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

        ConfigureAiUtility(context);

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
