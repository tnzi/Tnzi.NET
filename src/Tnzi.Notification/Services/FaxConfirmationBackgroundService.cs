namespace Tnzi.Notification.Services;

/// <summary>
/// 回执轮询后台服务：周期从收件箱取信、判读、把失败的那些落到收件人上。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>只有配了收件箱账号才会被注册</b>（判据是 <see cref="FaxConfirmationOptions.IsUsable"/>，
/// 与配置校验共用同一个属性）。没配的部署连这个类都不会被实例化 —— 这正是"可选"该有的样子：
/// 不是起一个每 5 分钟醒来发现自己没事干的线程。
/// </para>
/// <para>
/// <b>一轮失败只丢这一轮</b>：IMAP 连不上、网关那头抽风、判读器抛异常，都只记日志，
/// 下一轮照常再来。让后台服务崩掉会让此后所有回执都收不到，而这种停摆没有任何症状。
/// </para>
/// <para>
/// <b>判读器返回 <c>null</c> 的邮件不产生任何动作</b>，但它已经被标成已读了 ——
/// 这是有意的：收件箱里本来就有别的信，每轮重新判读一遍读不懂的旧信只是白费。
/// </para>
/// </remarks>
public class FaxConfirmationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<NotificationOptions> _options;
    private readonly ILogger<FaxConfirmationBackgroundService> _logger;

    /// <summary>启动后的首次轮询延迟，给宿主留出迁移与预热的时间。</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(45);

    /// <summary>配置读不出来时的兜底间隔。</summary>
    private static readonly TimeSpan FallbackInterval = TimeSpan.FromMinutes(5);

    /// <summary>初始化一个 <see cref="FaxConfirmationBackgroundService"/> 实例。</summary>
    public FaxConfirmationBackgroundService(
        IServiceProvider serviceProvider,
        IOptionsMonitor<NotificationOptions> options,
        ILogger<FaxConfirmationBackgroundService> logger)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var confirmation = _options.CurrentValue.FaxSender?.Confirmation;

            // 运行期被关掉（IOptionsMonitor 热更）时安静地空转，而不是退出循环 ——
            // 退出了就再也开不回来，除非重启进程。
            if (confirmation is { IsUsable: true })
            {
                try
                {
                    await PollAsync(confirmation, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fax confirmation poll failed; will retry on the next cycle");
                }
            }

            var interval = confirmation is { PollIntervalSeconds: > 0 }
                ? TimeSpan.FromSeconds(confirmation.PollIntervalSeconds)
                : FallbackInterval;

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// 取一批回执、判读、落库。<c>internal</c> 是为了让"一封坏信不拖累其余"这条不变量测得到 ——
    /// 从 <see cref="ExecuteAsync"/> 走要先等 45 秒启动延迟。
    /// </summary>
    internal async Task PollAsync(FaxConfirmationOptions confirmation, CancellationToken cancellationToken)
    {
        IReadOnlyList<FaxConfirmationMessage> messages;
        using (var fetchScope = _serviceProvider.CreateScope())
        {
            var mailbox = fetchScope.ServiceProvider.GetRequiredService<IFaxConfirmationMailbox>();
            messages = await mailbox.FetchAsync(confirmation.MaxMessagesPerPoll, cancellationToken);
        }

        if (messages.Count == 0)
            return;

        var applied = 0;
        var unrecognised = 0;
        var failed = 0;

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ★★ **一封信一个 scope，一封信一个 try**，两条都不是洁癖：
            // ① 取信那一步已经把这一批标成已读了，所以一封坏信让整个 foreach 中断
            //    等于把**剩下的回执永久丢掉** —— 它们不会再出现在下一轮的未读里。
            // ② 共用一个 DbContext 更糟：一次 SaveChanges 失败后，那个改坏的实体仍留在
            //    ChangeTracker 里，下一封信的 SaveChanges 会把它一起重放到一个毫不相干的地方。
            using var scope = _serviceProvider.CreateScope();

            try
            {
                var parsed = scope.ServiceProvider.GetRequiredService<IFaxConfirmationParser>().Parse(message);
                if (parsed == null)
                {
                    unrecognised++;
                    continue;
                }

                var result = await scope.ServiceProvider
                    .GetRequiredService<IFaxConfirmationService>()
                    .ApplyAsync(parsed, cancellationToken);

                if (result.Succeeded && result.Data)
                    applied++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "Failed to process one fax confirmation (subject: {Subject})", message.Subject);
            }
        }

        _logger.LogInformation(
            "Fax confirmations: read {Read}, recorded {Applied} delivery failure(s), {Unrecognised} not recognised, {Failed} errored",
            messages.Count, applied, unrecognised, failed);
    }
}
