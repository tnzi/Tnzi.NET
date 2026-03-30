using Tnzi.AI;
using Tnzi.AI.Channels.Adapters.Feishu;
using Tnzi.AI.Channels.Adapters.Telegram;

namespace Tnzi.AI.Channels;

/// <summary>
/// IM Channel Bridge 模块 — 将 AI Agent 连接到 Telegram/Feishu/DingTalk 等 IM 平台
/// </summary>
[DependsOn(typeof(AIModule))]
public class ChannelsModule : TnziApplicationModule
{
    public override int LoadOrder => 58;
    public override string? TableNamePrefix => "AI";

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddOptions<ChannelsModuleOptions>()
            .Bind(context.Configuration.GetSection("AI:Channels"))
            .ValidateWith<ChannelsModuleOptions, ChannelsModuleOptionsValidator>();
        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var options = context.Configuration.GetSection("AI:Channels").Get<ChannelsModuleOptions>() ?? new();
        if (!options.Enabled) return Task.CompletedTask;

        var services = context.Services;

        // 消息总线（Singleton — 进程内 Channel<T> 队列）
        services.AddSingleton<IChannelMessageBus, InMemoryChannelMessageBus>();

        // 线程映射存储
        if (string.Equals(options.ThreadStore, "File", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IChannelThreadStore>(sp =>
                new FileChannelThreadStore(sp.GetRequiredService<ILogger<FileChannelThreadStore>>(), options.FileStorePath));
        }
        else
        {
            services.AddScoped<IChannelThreadStore, DatabaseChannelThreadStore>();
        }

        // 消息调度管理器
        services.AddSingleton<IChannelManager, ChannelManager>();

        // Telegram 适配器
        if (options.Telegram.Enabled)
        {
            services.AddSingleton<IChannelAdapter, TelegramChannelAdapter>();
        }

        // Feishu 适配器
        if (options.Feishu.Enabled)
        {
            services.AddSingleton<IChannelAdapter, FeishuChannelAdapter>();
        }

        // HostedService — 绑定 Manager + Adapters 生命周期
        services.AddHostedService<ChannelManagerHostedService>();

        return Task.CompletedTask;
    }
}
