namespace Tnzi.Audit.Tests.Middleware;

/// <summary>
/// AuditMiddleware 实体级审计挂载测试 — collector drain 后挂到 AuditOperation.EntityEntries
/// </summary>
public class AuditMiddlewareEntityEntriesTests
{
    private sealed class StaticOptionsMonitor(AuditOptions value) : IOptionsMonitor<AuditOptions>
    {
        public AuditOptions CurrentValue => value;
        public AuditOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<AuditOptions, string?> listener) => null;
    }

    private sealed class CapturingAuditSender : IAuditSender
    {
        public List<AuditOperation> Captured { get; } = [];

        public Task SendAsync(AuditOperation operation)
        {
            Captured.Add(operation);
            return Task.CompletedTask;
        }
    }

    private static AuditEntityEntry MakeEntry() => new()
    {
        EntityTypeName = "Product",
        EntityTypeFullName = "Test.Product",
        EntityId = Guid.NewGuid().ToString(),
        OperationType = Tnzi.Audit.Metadata.EntityState.Modified,
        CreationTime = DateTime.UtcNow
    };

    private static (AuditMiddleware Middleware, CapturingAuditSender Sender) CreateMiddleware(AuditOptions? options = null)
    {
        var sender = new CapturingAuditSender();
        var middleware = new AuditMiddleware(
            _ => Task.CompletedTask,
            NullLogger<AuditMiddleware>.Instance,
            sender,
            new StaticOptionsMonitor(options ?? new AuditOptions()),
            new RequestBodyRedactor());
        return (middleware, sender);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "PUT";
        context.Request.Path = "/test/resource";
        return context;
    }

    [Fact]
    public async Task InvokeAsync_AttachesDrainedEntityEntries_ToAuditOperation()
    {
        var (middleware, sender) = CreateMiddleware();
        var collector = new EntityAuditCollector();
        collector.AddRange([MakeEntry(), MakeEntry()]);
        var currentUser = new Mock<ICurrentUser>();

        await middleware.InvokeAsync(CreateHttpContext(), currentUser.Object, collector);

        var operation = sender.Captured.ShouldHaveSingleItem();
        operation.EntityEntries.Count.ShouldBe(2);
        // drain 语义：挂载后 collector 清空
        collector.HasEntries.ShouldBeFalse();
    }

    [Fact]
    public async Task InvokeAsync_WithoutEntityChanges_LeavesEntityEntriesEmpty()
    {
        var (middleware, sender) = CreateMiddleware();
        var currentUser = new Mock<ICurrentUser>();

        await middleware.InvokeAsync(CreateHttpContext(), currentUser.Object, new EntityAuditCollector());

        sender.Captured.ShouldHaveSingleItem().EntityEntries.ShouldBeEmpty();
    }
}
