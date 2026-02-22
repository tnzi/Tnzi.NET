using HubOptions = Tnzi.SignalR.Options.HubOptions;

namespace Tnzi.SignalR;

/// <summary>
/// SignalR模块
/// SignalR 是 ASP.NET Core 的一部分，需要在 AspNetCore 模块之后加载
/// 配置路径：SignalR
/// </summary>
[DependsOn(typeof(AspNetCoreModule))]
public class SignalRModule : TnziFrameworkModule
{
    /// <summary>
    /// 在 AspNetCore 模块之后加载
    /// </summary>
    public override int LoadOrder => 20;

    /// <summary>
    /// 预配置服务
    /// </summary>
    /// <param name="context">服务配置上下文</param>
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册配置选项并启用启动时验证
        context.Services.AddOptions<SignalROptions>()
            .Bind(context.Configuration.GetSection("SignalR"))
            .ValidateWith<SignalROptions, SignalROptionsValidator>();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 配置服务
    /// </summary>
    /// <param name="context">服务配置上下文</param>
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var configuration = context.Configuration;

        // 获取配置选项
        var signalROptions = configuration
            .GetSection("SignalR")
            .Get<SignalROptions>() ?? new SignalROptions();
        var rateLimitOptions = signalROptions.RateLimit ?? new RateLimitOptions();

        // 注册 SignalR 及其 Filters
        var signalRBuilder = services.AddSignalR(options =>
        {
            // 应用 Hub 配置（确保 Hub 不为 null）
            var hubOptions = signalROptions.Hub ?? new HubOptions();
            options.EnableDetailedErrors = hubOptions.EnableDetailedErrors;
            options.KeepAliveInterval = hubOptions.KeepAliveInterval;
            options.ClientTimeoutInterval = hubOptions.ClientTimeoutInterval;
            options.HandshakeTimeout = hubOptions.HandshakeTimeout;
            options.MaximumReceiveMessageSize = hubOptions.MaximumReceiveMessageSize;

            // 注册 Filters (按顺序: 异常处理 -> 授权 -> 速率限制(可选) -> 日志(可选))
            options.AddFilter<ExceptionHandlingHubFilter>();
            options.AddFilter<HubAuthorizationFilter>();

            if (rateLimitOptions.Enabled)
            {
                options.AddFilter<RateLimitHubFilter>();
            }

            if (signalROptions.EnableDetailedLogging)
            {
                options.AddFilter<LoggingHubFilter>();
            }
        });

        // 配置 Redis Backplane（仅使用 SignalR:Backplane:ConnectionString，无回退）
        ConfigureBackplane(signalRBuilder, signalROptions.Backplane);

        // 配置 MessagePack 协议（确保 MessagePack 不为 null）
        var messagePackConfig = signalROptions.MessagePack ?? new MessagePackOptions();
        if (messagePackConfig.Enabled)
        {
            signalRBuilder.AddMessagePackProtocol(protocolOptions =>
            {
                var serializerOptions = MessagePackSerializerOptions.Standard;
                
                if (messagePackConfig.EnableCompression)
                {
                    serializerOptions = serializerOptions
                        .WithCompression(MessagePackCompression.Lz4BlockArray);
                }
                
                protocolOptions.SerializerOptions = serializerOptions;
            });
        }

        // 注册连接管理器
        services.AddSingleton<IConnectionManager, ConnectionManager>();

        // 当未加载 Authorization 模块时，提供 NullPermissionChecker，确保 HubAuthorizationFilter 可解析
        services.TryAddScoped<IPermissionChecker, NullPermissionChecker>();

        // 注意：消息推送服务需要应用程序自行注册，因为需要指定具体的 Hub 类型
        // 示例：
        // services.AddScoped<IMessagePushService<ChatHub>, MessagePushService<ChatHub>>();
        // services.AddScoped<IMessagePushService>(sp => sp.GetRequiredService<IMessagePushService<ChatHub>>());

        // 注册速率限制服务 (如果启用)
        if (rateLimitOptions.Enabled)
        {
            services.AddSingleton<IRateLimitService, RateLimitService>();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 配置 Backplane。Redis 时仅使用 SignalR:Backplane:ConnectionString，不使用其它模块的 Redis 配置。
    /// </summary>
    /// <param name="builder">SignalR 服务构建器</param>
    /// <param name="options">Backplane 配置选项</param>
    private static void ConfigureBackplane(
        ISignalRServerBuilder builder,
        BackplaneOptions? options)
    {
        if (options == null || options.Type == BackplaneType.None)
        {
            return;
        }

        if (options.Type == BackplaneType.Redis)
        {
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new ConfigurationException(
                    "SignalR:Backplane:ConnectionString",
                    "Redis connection string is required when using Redis backplane. Configure SignalR:Backplane:ConnectionString.");
            }

            builder.AddStackExchangeRedis(options.ConnectionString, redisOptions =>
            {
                redisOptions.Configuration.ChannelPrefix =
                    RedisChannel.Literal(options.ChannelPrefix);
            });
        }
    }

    /// <summary>
    /// 应用初始化
    /// </summary>
    /// <param name="context">应用初始化上下文</param>
    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var app = context.App;
        if (app != null)
        {
            // SignalR Hub 路由配置将在使用此模块的应用中完成
            // 示例: app.MapHub<ChatHub>("/hubs/chat");
        }

        return Task.CompletedTask;
    }
}