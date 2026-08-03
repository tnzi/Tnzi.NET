
namespace Tnzi.Identity.Services;

/// <summary>
/// 登录日志后台处理服务
/// </summary>
public class LoginLogBackgroundService : ChannelBatchProcessorBase<LoginLog>
{
    public LoginLogBackgroundService(
        ILoginLogConsumer consumer,
        IServiceProvider serviceProvider,
        ILogger<LoginLogBackgroundService> logger)
        : base(Check.NotNull(consumer).Reader, serviceProvider, logger)
    {
    }

    /// <inheritdoc />
    protected override int BatchSize => 50;

    /// <inheritdoc />
    protected override async Task ProcessBatchAsync(
        IReadOnlyList<LoginLog> batch,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken)
    {
        var repository = scopedServices.GetRequiredService<IRepository<LoginLog, Guid>>();
        await repository.InsertManyAsync(batch);
    }
}
