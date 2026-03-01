
namespace Tnzi.RabbitMQ;

/// <summary>
/// RabbitMQ 事件总线模块
/// 配置路径：RabbitMQ（可选）、EventBus（必需）
/// 注意：此模块仅在 EventBus.Type = "RabbitMQ" 时才会注册 RabbitMQ 相关服务
/// </summary>
[DependsOn(typeof(EventBusModule))]
public class RabbitMQEventBusModule : TnziInfrastructureModule
{
    /// <summary>
    /// 在 EventBusModule 之后加载
    /// </summary>
    public override int LoadOrder => 11;

    /// <summary>
    /// 预配置服务
    /// </summary>
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册配置选项并启用启动时验证
        context.Services.AddOptions<RabbitMQOptions>()
            .Bind(context.Configuration.GetSection("RabbitMQ"))
            .ValidateWith<RabbitMQOptions, RabbitMQOptionsValidator>();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 配置服务
    /// </summary>
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var configuration = context.Configuration;

        // 检查 EventBus.Type 是否为 RabbitMQ
        var eventBusOptions = configuration.GetSection("EventBus").Get<EventBusOptions>() ?? new EventBusOptions();

        if (!string.Equals(eventBusOptions.Type, "RabbitMQ", StringComparison.OrdinalIgnoreCase))
        {
            // 如果类型不是 RabbitMQ，不注册 RabbitMQ 相关服务
            return Task.CompletedTask;
        }

        // 获取连接字符串（优先级：EventBusOptions.RabbitMqConnectionString > ConnectionStrings:RabbitMQ）
        var connectionString = eventBusOptions.RabbitMqConnectionString
            ?? configuration.GetConnectionString("RabbitMQ")
            ?? throw new RabbitMQConnectionException(
                "RabbitMQ connection string is required when EventBus.Type is RabbitMQ. " +
                "Configure EventBus.RabbitMqConnectionString or ConnectionStrings:RabbitMQ in your configuration file.");

        // 验证连接字符串格式
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new RabbitMQConnectionException("RabbitMQ connection string cannot be empty.");
        }

        try
        {
            // 验证 URI 格式
            _ = new Uri(connectionString);
        }
        catch (UriFormatException ex)
        {
            throw new RabbitMQConnectionException(
                "Invalid RabbitMQ connection string format. " +
                "Connection string must be a valid URI (e.g., amqp://user:pass@localhost:5672/).",
                innerException: ex);
        }

        // 获取交换机名称（优先级：EventBusOptions.RabbitMqExchangeName > 默认值）
        var exchangeName = eventBusOptions.RabbitMqExchangeName ?? "Tnzi.Events";

        // 注册 RabbitMQ 连接工厂
        services.AddSingleton<IConnectionFactory>(provider =>
        {
            var rabbitMQOptions = provider.GetService<IOptions<RabbitMQOptions>>()?.Value ?? new RabbitMQOptions();
            var connOptions = rabbitMQOptions.Connection ?? new ConnectionOptions();

            var factory = new ConnectionFactory
            {
                Uri = new Uri(connectionString),
                AutomaticRecoveryEnabled = connOptions.AutomaticRecoveryEnabled,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(connOptions.NetworkRecoveryIntervalSeconds),
                RequestedConnectionTimeout = TimeSpan.FromMilliseconds(connOptions.RequestedConnectionTimeout),
                RequestedHeartbeat = TimeSpan.FromSeconds(connOptions.RequestedHeartbeat)
            };
            return factory;
        });

        // 注册 RabbitMQ 连接（RabbitMQ.Client 7.0+ 使用异步方法）
        services.AddSingleton<IConnection>(provider =>
        {
            var factory = provider.GetRequiredService<IConnectionFactory>();
            var logger = provider.GetService<ILogger<RabbitMQEventBusModule>>();

            try
            {
                var connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
                logger?.LogInformation("Successfully connected to RabbitMQ at {Endpoint}", SanitizeAmqpUri(factory.Uri));
                return connection;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to connect to RabbitMQ at {Endpoint}", SanitizeAmqpUri(factory.Uri));
                throw new RabbitMQConnectionException(
                    $"Failed to connect to RabbitMQ at {SanitizeAmqpUri(factory.Uri)}",
                    SanitizeAmqpUri(factory.Uri),
                    ex);
            }
        });

        // 注册 RabbitMQEventBus 实例（具名 Singleton）
        services.AddSingleton<RabbitMQEventBus>(provider =>
        {
            var connection = provider.GetRequiredService<IConnection>();
            var logger = provider.GetRequiredService<ILogger<RabbitMQEventBus>>();
            var rabbitOptions = provider.GetService<IOptions<RabbitMQOptions>>()?.Value ?? new RabbitMQOptions();

            return new RabbitMQEventBus(connection, logger, provider, rabbitOptions, exchangeName);
        });

        // 注册 IEventBus 和 IIntegrationEventBus 接口，指向同一实例（与 Kafka 模块一致）
        services.RemoveAll<IEventBus>();
        services.AddSingleton<IEventBus>(provider => provider.GetRequiredService<RabbitMQEventBus>());
        services.AddSingleton<IIntegrationEventBus>(provider => provider.GetRequiredService<RabbitMQEventBus>());

        return Task.CompletedTask;
    }

    /// <summary>
    /// 应用关闭时清理资源
    /// </summary>
    public override async Task OnApplicationShutdownAsync(ApplicationShutdownContext context)
    {
        // 优先使用 IAsyncDisposable 优雅关闭 EventBus（等待消费者完成）
        var eventBus = context.ServiceProvider.GetService<RabbitMQEventBus>();
        if (eventBus != null)
        {
            try
            {
                await eventBus.DisposeAsync();
            }
            catch (Exception ex)
            {
                var logger = context.ServiceProvider.GetService<ILogger<RabbitMQEventBusModule>>();
                logger?.LogError(ex, "Error occurred while disposing RabbitMQ event bus");
            }
        }

        // 关闭连接
        try
        {
            var connection = context.ServiceProvider.GetService<IConnection>();
            if (connection != null)
            {
                connection.Dispose();
            }
        }
        catch (Exception ex)
        {
            var logger = context.ServiceProvider.GetService<ILogger<RabbitMQEventBusModule>>();
            logger?.LogError(ex, "Error occurred while closing RabbitMQ connection");
        }
    }

    /// <summary>
    /// 从 AMQP URI 中提取 host:port，移除用户名密码等敏感信息
    /// </summary>
    private static string SanitizeAmqpUri(Uri? uri)
    {
        if (uri == null) return "(unknown)";
        try
        {
            return $"{uri.Host}:{uri.Port}";
        }
        catch
        {
            return "(invalid URI)";
        }
    }
}
