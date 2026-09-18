using Tnzi.MultiTenancy;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// 队列工作项自己带着租户：<see cref="NotificationWorkItem.RunAsync"/> 在交来的作用域里先切进
/// 捕获的租户再执行。这是三条入队路径（排队 / 定时 / 延迟重试）共用的那一处收口。
/// </summary>
/// <remarks>
/// ★ 这里的容器是<b>真的</b>：<c>ICurrentTenant</c> 按作用域注册为真实的 <see cref="CurrentTenant"/>，
/// 工作项在里面读到的租户就是 DbContext 会读到的那个。用 mock 只能证明 <c>Change</c> 被调过，
/// 证明不了切换对同一作用域里的其它服务生效。
/// </remarks>
public class NotificationWorkItemTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddLogging();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RunAsync_SwitchesTheScopeToTheCapturedTenant()
    {
        var tenantId = Guid.NewGuid();
        Guid? observed = Guid.Empty;
        var item = new NotificationWorkItem(tenantId, (sp, _) =>
        {
            observed = sp.GetRequiredService<ICurrentTenant>().Id;
            return Task.CompletedTask;
        });

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        await item.RunAsync(scope.ServiceProvider, CancellationToken.None);

        observed.ShouldBe(tenantId);
    }

    [Fact]
    public async Task RunAsync_LeavesTheScopeAsItFoundIt()
    {
        var item = new NotificationWorkItem(Guid.NewGuid(), (_, _) => Task.CompletedTask);

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        await item.RunAsync(scope.ServiceProvider, CancellationToken.None);

        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Id.ShouldBeNull();
    }

    /// <summary>没有租户的工作项（单租户部署、host 级消息）不切换，也不炸。</summary>
    [Fact]
    public async Task RunAsync_WithoutATenant_StillRunsTheWork()
    {
        var ran = false;
        var item = new NotificationWorkItem(null, (_, _) =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        await item.RunAsync(scope.ServiceProvider, CancellationToken.None);

        ran.ShouldBeTrue();
    }

    /// <summary>容器里没有 <c>ICurrentTenant</c>（消费方没装多租户基础设施）时照常执行。</summary>
    [Fact]
    public async Task RunAsync_WithoutACurrentTenantService_StillRunsTheWork()
    {
        var ran = false;
        var item = new NotificationWorkItem(Guid.NewGuid(), (_, _) =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        using var provider = new ServiceCollection().BuildServiceProvider();
        await item.RunAsync(provider, CancellationToken.None);

        ran.ShouldBeTrue();
    }

    /// <summary>
    /// ★★ 「发送这条消息」的工作项失败时要<b>留下日志</b>。此前 <c>Task&lt;Result&gt;</c> 隐式转成
    /// <c>Task</c> 后被整个丢掉 —— 这正是租户丢失那条缺陷零症状的原因。
    /// </summary>
    [Fact]
    public async Task SendMessage_LogsAFailedResult()
    {
        var messageId = Guid.NewGuid();
        var notificationService = new Mock<INotificationService>();
        notificationService
            .Setup(s => s.SendAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure($"Notification {messageId} not found", 404));
        var logger = new Mock<ILogger<NotificationWorkItem>>();

        using var provider = BuildProvider(services =>
        {
            services.AddSingleton(notificationService.Object);
            services.AddSingleton(logger.Object);
        });
        using var scope = provider.CreateScope();

        await NotificationWorkItem.SendMessage(messageId, tenantId: null).RunAsync(scope.ServiceProvider, CancellationToken.None);

        logger.Verify(l => l.Log(
                It.Is<LogLevel>(level => level >= LogLevel.Warning),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(messageId.ToString())),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendMessage_SaysNothingOnSuccess()
    {
        var messageId = Guid.NewGuid();
        var notificationService = new Mock<INotificationService>();
        notificationService
            .Setup(s => s.SendAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        var logger = new Mock<ILogger<NotificationWorkItem>>();

        using var provider = BuildProvider(services =>
        {
            services.AddSingleton(notificationService.Object);
            services.AddSingleton(logger.Object);
        });
        using var scope = provider.CreateScope();

        await NotificationWorkItem.SendMessage(messageId, tenantId: null).RunAsync(scope.ServiceProvider, CancellationToken.None);

        logger.Verify(l => l.Log(
                It.Is<LogLevel>(level => level >= LogLevel.Warning),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    /// <summary>
    /// ★ 真实的 <see cref="ChannelQueueService"/> 从根容器开新作用域执行工作项 ——
    /// 租户正是在这一步丢的。它必须经 <see cref="NotificationWorkItem.RunAsync"/> 执行，
    /// 而不是直接调那个委托。
    /// </summary>
    [Fact]
    public async Task ChannelQueueService_RunsEachWorkItemInsideItsTenant()
    {
        var tenantId = Guid.NewGuid();
        Guid? observed = Guid.Empty;
        var done = new TaskCompletionSource();

        using var provider = BuildProvider();
        var options = new Mock<IOptionsMonitor<NotificationOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new NotificationOptions());
        var queue = new ChannelQueueService(provider, provider.GetRequiredService<ILogger<ChannelQueueService>>(), options.Object);

        await queue.EnqueueAsync(new NotificationWorkItem(tenantId, (sp, _) =>
        {
            observed = sp.GetRequiredService<ICurrentTenant>().Id;
            done.TrySetResult();
            return Task.CompletedTask;
        }));

        using var cts = new CancellationTokenSource();
        var running = queue.StartAsync(cts.Token);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();
        try { await running; } catch (OperationCanceledException) { }

        observed.ShouldBe(tenantId);
    }
}
