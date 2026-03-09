using Tnzi.MultiTenancy;

namespace Tnzi.Tests.BackgroundJobs;

/// <summary>
/// TenantAwareBackgroundJob 测试类
/// </summary>
public class TenantAwareBackgroundJobTests
{
    // -----------------------------------------------------------------
    // 测试用具体实现
    // -----------------------------------------------------------------

    /// <summary>
    /// 实现 ITenantAwareJobArgs 的测试参数类
    /// </summary>
    public class TestJobArgs : ITenantAwareJobArgs
    {
        public Guid? TenantId { get; set; }
        public string Payload { get; set; } = string.Empty;
    }

    /// <summary>
    /// 用于测试的具体后台任务，记录执行时的快照信息
    /// </summary>
    public class TestTenantAwareJob : TenantAwareBackgroundJob<TestJobArgs>
    {
        public bool WasExecuted { get; private set; }
        public TestJobArgs? ReceivedArgs { get; private set; }

        public TestTenantAwareJob(ICurrentTenant? currentTenant = null)
            : base(currentTenant)
        {
        }

        protected override Task ExecuteInTenantContextAsync(TestJobArgs args, CancellationToken cancellationToken)
        {
            WasExecuted = true;
            ReceivedArgs = args;
            return Task.CompletedTask;
        }
    }

    // -----------------------------------------------------------------
    // 1. ITenantAwareJobArgs 接口契约测试
    // -----------------------------------------------------------------

    [Fact]
    public void ITenantAwareJobArgs_TenantId_PropertyExists_AndIsNullableGuid()
    {
        // Arrange
        var type = typeof(ITenantAwareJobArgs);
        var property = type.GetProperty(nameof(ITenantAwareJobArgs.TenantId));

        // Assert — 属性存在
        Assert.NotNull(property);
        // Assert — 类型是 Guid?
        Assert.Equal(typeof(Guid?), property.PropertyType);
        // Assert — 可读可写
        Assert.True(property.CanRead);
        Assert.True(property.CanWrite);
    }

    [Fact]
    public void ITenantAwareJobArgs_Implementation_CanSetAndGetTenantId()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var args = new TestJobArgs { TenantId = tenantId };

        // Assert
        Assert.Equal(tenantId, args.TenantId);
    }

    [Fact]
    public void ITenantAwareJobArgs_Implementation_TenantId_CanBeNull()
    {
        // Arrange
        var args = new TestJobArgs { TenantId = null };

        // Assert
        Assert.Null(args.TenantId);
    }

    // -----------------------------------------------------------------
    // 2. 携带 TenantId 时，执行在租户上下文中（ICurrentTenant.Change 被调用）
    // -----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithTenantId_CallsCurrentTenantChange()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var args = new TestJobArgs { TenantId = tenantId };

        var mockDisposable = new Mock<IDisposable>();
        var mockCurrentTenant = new Mock<ICurrentTenant>();
        mockCurrentTenant
            .Setup(t => t.Change(tenantId, null))
            .Returns(mockDisposable.Object);

        var job = new TestTenantAwareJob(mockCurrentTenant.Object);

        // Act
        await job.ExecuteAsync(args);

        // Assert — Change 被调用一次，传入正确的 tenantId
        mockCurrentTenant.Verify(t => t.Change(tenantId, null), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithTenantId_ExecutesInnerLogic()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var args = new TestJobArgs { TenantId = tenantId, Payload = "hello" };

        var mockDisposable = new Mock<IDisposable>();
        var mockCurrentTenant = new Mock<ICurrentTenant>();
        mockCurrentTenant
            .Setup(t => t.Change(tenantId, null))
            .Returns(mockDisposable.Object);

        var job = new TestTenantAwareJob(mockCurrentTenant.Object);

        // Act
        await job.ExecuteAsync(args);

        // Assert — 内部方法确实被执行，参数正确传入
        Assert.True(job.WasExecuted);
        Assert.Same(args, job.ReceivedArgs);
    }

    // -----------------------------------------------------------------
    // 3. TenantId 为 null 时，不切换租户上下文，直接执行
    // -----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithNullTenantId_DoesNotCallCurrentTenantChange()
    {
        // Arrange
        var args = new TestJobArgs { TenantId = null, Payload = "no-tenant" };

        var mockCurrentTenant = new Mock<ICurrentTenant>();
        var job = new TestTenantAwareJob(mockCurrentTenant.Object);

        // Act
        await job.ExecuteAsync(args);

        // Assert — Change 不应被调用
        mockCurrentTenant.Verify(t => t.Change(It.IsAny<Guid?>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullTenantId_StillExecutesInnerLogic()
    {
        // Arrange
        var args = new TestJobArgs { TenantId = null, Payload = "no-tenant" };

        var mockCurrentTenant = new Mock<ICurrentTenant>();
        var job = new TestTenantAwareJob(mockCurrentTenant.Object);

        // Act
        await job.ExecuteAsync(args);

        // Assert — 业务逻辑仍然执行
        Assert.True(job.WasExecuted);
        Assert.Same(args, job.ReceivedArgs);
    }

    // -----------------------------------------------------------------
    // 4. ICurrentTenant 为 null 时，仍正常执行（不抛异常）
    // -----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithNullCurrentTenant_AndTenantId_StillExecutes()
    {
        // Arrange — ICurrentTenant 未注册（null）
        var args = new TestJobArgs { TenantId = Guid.NewGuid(), Payload = "orphan" };
        var job = new TestTenantAwareJob(currentTenant: null);

        // Act — 不应抛出任何异常
        await job.ExecuteAsync(args);

        // Assert
        Assert.True(job.WasExecuted);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullCurrentTenant_AndNullTenantId_StillExecutes()
    {
        // Arrange
        var args = new TestJobArgs { TenantId = null };
        var job = new TestTenantAwareJob(currentTenant: null);

        // Act
        await job.ExecuteAsync(args);

        // Assert
        Assert.True(job.WasExecuted);
    }

    // -----------------------------------------------------------------
    // 5. 执行完成后，租户上下文被正确还原（Dispose 被调用）
    // -----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithTenantId_DisposesContextAfterExecution()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var args = new TestJobArgs { TenantId = tenantId };

        var mockDisposable = new Mock<IDisposable>();
        var mockCurrentTenant = new Mock<ICurrentTenant>();
        mockCurrentTenant
            .Setup(t => t.Change(tenantId, null))
            .Returns(mockDisposable.Object);

        var job = new TestTenantAwareJob(mockCurrentTenant.Object);

        // Act
        await job.ExecuteAsync(args);

        // Assert — using 块结束后 Dispose 应被调用一次，确保上下文恢复
        mockDisposable.Verify(d => d.Dispose(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithTenantId_DisposesContextEvenAfterException()
    {
        // Arrange — 内部抛出异常时，using 块依然会 Dispose
        var tenantId = Guid.NewGuid();
        var args = new TestJobArgs { TenantId = tenantId };

        var mockDisposable = new Mock<IDisposable>();
        var mockCurrentTenant = new Mock<ICurrentTenant>();
        mockCurrentTenant
            .Setup(t => t.Change(tenantId, null))
            .Returns(mockDisposable.Object);

        // 使用会抛异常的具体任务
        var job = new ThrowingTenantJob(mockCurrentTenant.Object);

        // Act & Assert — 异常向上传播
        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync(args));

        // Assert — Dispose 仍然被调用（using 语义保证）
        mockDisposable.Verify(d => d.Dispose(), Times.Once);
    }

    /// <summary>
    /// 辅助类：ExecuteInTenantContextAsync 内部抛出异常，用于测试 Dispose 保证
    /// </summary>
    private sealed class ThrowingTenantJob : TenantAwareBackgroundJob<TestJobArgs>
    {
        public ThrowingTenantJob(ICurrentTenant? currentTenant = null) : base(currentTenant) { }

        protected override Task ExecuteInTenantContextAsync(TestJobArgs args, CancellationToken cancellationToken)
            => throw new InvalidOperationException("业务异常");
    }

    // -----------------------------------------------------------------
    // 6. CancellationToken 正确传递给内部执行方法
    // -----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_PassesCancellationTokenToInnerMethod()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var args = new TestJobArgs { TenantId = null };

        var captureJob = new CancelTokenCaptureJob();

        // Act
        await captureJob.ExecuteAsync(args, cts.Token);

        // Assert
        Assert.Equal(cts.Token, captureJob.CapturedToken);
    }

    /// <summary>
    /// 辅助类：捕获传入的 CancellationToken
    /// </summary>
    private sealed class CancelTokenCaptureJob : TenantAwareBackgroundJob<TestJobArgs>
    {
        public CancellationToken CapturedToken { get; private set; }

        public CancelTokenCaptureJob() : base(null) { }

        protected override Task ExecuteInTenantContextAsync(TestJobArgs args, CancellationToken cancellationToken)
        {
            CapturedToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
