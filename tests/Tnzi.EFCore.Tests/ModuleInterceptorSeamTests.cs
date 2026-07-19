using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// AddTnziDbContext 的模块拦截器 seam 测试：
/// 模块把拦截器注册为 IInterceptor 服务后自动挂进 Tnzi DbContext
/// （Tnzi.Audit 的实体级审计拦截器即经此 seam 生效）。
/// </summary>
public class ModuleInterceptorSeamTests : IDisposable
{
    private sealed class RecordingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public int SavingChangesCalls { get; private set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            SavingChangesCalls++;
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            SavingChangesCalls++;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private readonly SqliteConnection _connection;

    public ModuleInterceptorSeamTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AddTnziDbContext_AttachesDiRegisteredIInterceptor_ToDbContext()
    {
        var recorder = new RecordingSaveChangesInterceptor();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<ICurrentTenant>(new MockCurrentTenant());
        // 模拟模块（如 Tnzi.Audit）把拦截器注册为 IInterceptor 服务
        services.AddScoped<IInterceptor>(_ => recorder);
        services.AddTnziDbContext<TestDbContext>(options => options.UseSqlite(_connection));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await context.Database.EnsureCreatedAsync();

        context.Products.Add(new TestProduct { Name = "SeamProduct", Price = 1m });
        await context.SaveChangesAsync();

        Assert.True(recorder.SavingChangesCalls > 0, "DI-registered IInterceptor should be attached and invoked on SaveChanges");
    }

    [Fact]
    public async Task AddTnziDbContext_WithoutRegisteredInterceptors_StillWorks()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<ICurrentTenant>(new MockCurrentTenant());
        services.AddTnziDbContext<TestDbContext>(options => options.UseSqlite(_connection));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await context.Database.EnsureCreatedAsync();

        context.Products.Add(new TestProduct { Name = "NoSeamProduct", Price = 1m });
        var affected = await context.SaveChangesAsync();

        Assert.Equal(1, affected);
    }
}
