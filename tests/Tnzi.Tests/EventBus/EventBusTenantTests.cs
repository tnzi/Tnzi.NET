
using Tnzi.MultiTenancy;

namespace Tnzi.Tests.EventBus;

/// <summary>
/// EventBus 多租户感知相关测试
/// </summary>
public class EventBusTenantTests
{
    // ------------------------------------------------------------------
    // 测试辅助类型
    // ------------------------------------------------------------------

    public class TenantTestEvent : EventBase
    {
        public string Payload { get; set; } = string.Empty;
    }

    /// <summary>
    /// 普通同步处理器：记录收到的事件
    /// </summary>
    public class RecordingEventHandler : IEventHandler<TenantTestEvent>
    {
        public TenantTestEvent? ReceivedEvent { get; private set; }

        public Task HandleAsync(TenantTestEvent @event, CancellationToken cancellationToken = default)
        {
            ReceivedEvent = @event;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 通知容器：用 Singleton 将 TCS 传入后台处理器，避免 DI 无法解析 TaskCompletionSource
    /// </summary>
    public class BackgroundHandlerSignal
    {
        public TaskCompletionSource<Guid?> Tcs { get; } =
            new TaskCompletionSource<Guid?>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// 后台处理器：记录执行时当前租户 ID，并通过 BackgroundHandlerSignal 通知测试。
    /// 必须同时以 IEventHandler&lt;TenantTestEvent&gt; 和具体类型注册，
    /// 因为 LocalEventBus 后台路径通过具体类型从 DI 解析。
    /// </summary>
    [BackgroundEventHandler]
    public class BackgroundTenantCapturingHandler : IEventHandler<TenantTestEvent>
    {
        private readonly ICurrentTenant? _currentTenant;
        private readonly BackgroundHandlerSignal _signal;

        public BackgroundTenantCapturingHandler(
            BackgroundHandlerSignal signal,
            ICurrentTenant? currentTenant = null)
        {
            _currentTenant = currentTenant;
            _signal = signal;
        }

        public Task HandleAsync(TenantTestEvent @event, CancellationToken cancellationToken = default)
        {
            _signal.Tcs.TrySetResult(_currentTenant?.Id);
            return Task.CompletedTask;
        }
    }

    // ------------------------------------------------------------------
    // 辅助工厂：构建带租户上下文的 LocalEventBus
    // ICurrentTenant 通过 DI 注册为 Scoped，由 LocalEventBus 在 PublishAsync 中从 scope 解析
    // ------------------------------------------------------------------

    private static (LocalEventBus bus, ServiceProvider sp) BuildBus(
        Mock<ICurrentTenant>? tenantMock = null,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // 注册 ICurrentTenant 为 Scoped（与生产环境一致）
        if (tenantMock != null)
        {
            services.AddScoped<ICurrentTenant>(_ => tenantMock.Object);
        }

        configure?.Invoke(services);

        var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILogger<LocalEventBus>>();
        var bus = new LocalEventBus(sp, logger);
        return (bus, sp);
    }

    // ------------------------------------------------------------------
    // 1. EventBase.TenantId — 属性可设置且可为 null
    // ------------------------------------------------------------------

    [Fact]
    public void TenantId_SetToGuid_ReturnsCorrectValue()
    {
        // Arrange
        var expected = Guid.NewGuid();
        var @event = new TenantTestEvent();

        // Act
        @event.TenantId = expected;

        // Assert
        Assert.Equal(expected, @event.TenantId);
    }

    [Fact]
    public void TenantId_SetToNull_ReturnsNull()
    {
        // Arrange
        var @event = new TenantTestEvent { TenantId = Guid.NewGuid() };

        // Act
        @event.TenantId = null;

        // Assert
        Assert.Null(@event.TenantId);
    }

    [Fact]
    public void TenantId_DefaultValue_IsNull()
    {
        // Arrange & Act
        var @event = new TenantTestEvent();

        // Assert
        Assert.Null(@event.TenantId);
    }

    // ------------------------------------------------------------------
    // 2. PublishAsync — 有租户时自动将租户 ID 写入 EventBase.TenantId
    // ------------------------------------------------------------------

    [Fact]
    public async Task PublishAsync_WithActiveTenant_CapturesTenantIdToEvent()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var mockTenant = new Mock<ICurrentTenant>();
        mockTenant.Setup(t => t.Id).Returns(tenantId);

        var handler = new RecordingEventHandler();
        var (bus, _) = BuildBus(
            tenantMock: mockTenant,
            configure: svc => svc.AddScoped<IEventHandler<TenantTestEvent>>(_ => handler));

        var @event = new TenantTestEvent { Payload = "hello" };

        // Act
        await bus.PublishAsync(@event);

        // Assert — 发布后事件上的 TenantId 应等于当前租户 ID
        Assert.Equal(tenantId, @event.TenantId);
        Assert.NotNull(handler.ReceivedEvent);
        Assert.Equal(tenantId, handler.ReceivedEvent!.TenantId);
    }

    // ------------------------------------------------------------------
    // 3. PublishAsync — 无租户时 TenantId 保持 null
    // ------------------------------------------------------------------

    [Fact]
    public async Task PublishAsync_WithNoActiveTenant_TenantIdRemainsNull()
    {
        // Arrange
        var mockTenant = new Mock<ICurrentTenant>();
        mockTenant.Setup(t => t.Id).Returns((Guid?)null);

        var handler = new RecordingEventHandler();
        var (bus, _) = BuildBus(
            tenantMock: mockTenant,
            configure: svc => svc.AddScoped<IEventHandler<TenantTestEvent>>(_ => handler));

        var @event = new TenantTestEvent { Payload = "no-tenant" };

        // Act
        await bus.PublishAsync(@event);

        // Assert
        Assert.Null(@event.TenantId);
    }

    [Fact]
    public async Task PublishAsync_WithNullCurrentTenant_TenantIdRemainsNull()
    {
        // Arrange — 未注册 ICurrentTenant（多租户模块未加载场景）
        var handler = new RecordingEventHandler();
        var (bus, _) = BuildBus(
            tenantMock: null,
            configure: svc => svc.AddScoped<IEventHandler<TenantTestEvent>>(_ => handler));

        var @event = new TenantTestEvent { Payload = "no-module" };

        // Act
        await bus.PublishAsync(@event);

        // Assert
        Assert.Null(@event.TenantId);
    }

    // ------------------------------------------------------------------
    // 4. PublishAsync — 事件已显式设置 TenantId 时，不覆盖
    // ------------------------------------------------------------------

    [Fact]
    public async Task PublishAsync_WhenTenantIdAlreadySet_DoesNotOverride()
    {
        // Arrange
        var explicitTenantId = Guid.NewGuid();
        var currentTenantId = Guid.NewGuid();

        var mockTenant = new Mock<ICurrentTenant>();
        mockTenant.Setup(t => t.Id).Returns(currentTenantId);

        var handler = new RecordingEventHandler();
        var (bus, _) = BuildBus(
            tenantMock: mockTenant,
            configure: svc => svc.AddScoped<IEventHandler<TenantTestEvent>>(_ => handler));

        // 事件已显式设置 TenantId
        var @event = new TenantTestEvent { TenantId = explicitTenantId };

        // Act
        await bus.PublishAsync(@event);

        // Assert — 保持显式设置的值，不被 ICurrentTenant 覆盖
        Assert.Equal(explicitTenantId, @event.TenantId);
    }

    // ------------------------------------------------------------------
    // 5. 后台处理器 — 租户上下文从 EventBase.TenantId 恢复
    // ------------------------------------------------------------------

    [Fact]
    public async Task BackgroundHandler_WhenTenantIdSet_RestoresTenantContext()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var signal = new BackgroundHandlerSignal();

        // ICurrentTenant mock：Change() 记录被调用时传入的 tenantId
        Guid? changedTo = null;
        var mockTenant = new Mock<ICurrentTenant>();
        mockTenant.Setup(t => t.Id).Returns(tenantId);
        mockTenant
            .Setup(t => t.Change(It.IsAny<Guid?>(), It.IsAny<string?>()))
            .Returns<Guid?, string?>((id, _) =>
            {
                changedTo = id;
                return Mock.Of<IDisposable>();
            });

        var services = new ServiceCollection();
        services.AddLogging();
        // Singleton signal 跨 scope 共享
        services.AddSingleton(signal);
        // ICurrentTenant 注册为 Scoped（后台处理器的新 scope 会解析）
        services.AddScoped<ICurrentTenant>(_ => mockTenant.Object);
        // 同时以接口和具体类型注册，LocalEventBus 后台路径通过具体类型解析
        services.AddScoped<IEventHandler<TenantTestEvent>, BackgroundTenantCapturingHandler>();
        services.AddScoped<BackgroundTenantCapturingHandler>();

        var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILogger<LocalEventBus>>();
        var bus = new LocalEventBus(sp, logger);

        var @event = new TenantTestEvent { Payload = "bg-tenant" };

        // Act
        await bus.PublishAsync(@event);

        // 等待后台任务完成（最多 5 秒）
        var completedTask = await Task.WhenAny(signal.Tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(completedTask == signal.Tcs.Task, "后台处理器未在 5 秒内完成");

        // Assert — Change() 应以正确的 tenantId 被调用
        Assert.Equal(tenantId, changedTo);
    }

    // ------------------------------------------------------------------
    // 6. 后台处理器 — TenantId 为 null 时不切换租户上下文
    // ------------------------------------------------------------------

    [Fact]
    public async Task BackgroundHandler_WhenTenantIdIsNull_DoesNotCallTenantChange()
    {
        // Arrange
        var signal = new BackgroundHandlerSignal();

        var mockTenant = new Mock<ICurrentTenant>();
        // 当前租户没有 ID
        mockTenant.Setup(t => t.Id).Returns((Guid?)null);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddScoped<ICurrentTenant>(_ => mockTenant.Object);
        services.AddScoped<IEventHandler<TenantTestEvent>, BackgroundTenantCapturingHandler>();
        services.AddScoped<BackgroundTenantCapturingHandler>();

        var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILogger<LocalEventBus>>();
        var bus = new LocalEventBus(sp, logger);

        var @event = new TenantTestEvent { Payload = "no-tenant-bg" };

        // Act
        await bus.PublishAsync(@event);

        var completedTask = await Task.WhenAny(signal.Tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(completedTask == signal.Tcs.Task, "后台处理器未在 5 秒内完成");

        // Assert — TenantId 为 null 时不应调用 Change()
        mockTenant.Verify(t => t.Change(It.IsAny<Guid?>(), It.IsAny<string?>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // 7. 后台处理器 — 租户 scope 在处理器完成后被释放
    // ------------------------------------------------------------------

    [Fact]
    public async Task BackgroundHandler_AfterHandlerCompletes_TenantScopeIsDisposed()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var signal = new BackgroundHandlerSignal();

        var tenantScopeDisposable = new Mock<IDisposable>();
        var mockTenant = new Mock<ICurrentTenant>();
        mockTenant.Setup(t => t.Id).Returns(tenantId);
        mockTenant
            .Setup(t => t.Change(It.IsAny<Guid?>(), It.IsAny<string?>()))
            .Returns(tenantScopeDisposable.Object);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddScoped<ICurrentTenant>(_ => mockTenant.Object);
        services.AddScoped<IEventHandler<TenantTestEvent>, BackgroundTenantCapturingHandler>();
        services.AddScoped<BackgroundTenantCapturingHandler>();

        var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILogger<LocalEventBus>>();
        var bus = new LocalEventBus(sp, logger);

        var @event = new TenantTestEvent { Payload = "dispose-check" };

        // Act
        await bus.PublishAsync(@event);

        // 等待后台任务完成
        var completedTask = await Task.WhenAny(signal.Tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(completedTask == signal.Tcs.Task, "后台处理器未在 5 秒内完成");

        // 给 finally 块一点时间执行
        await Task.Delay(50);

        // Assert — 租户 scope 的 Dispose() 必须被调用
        tenantScopeDisposable.Verify(d => d.Dispose(), Times.Once);
    }
}
