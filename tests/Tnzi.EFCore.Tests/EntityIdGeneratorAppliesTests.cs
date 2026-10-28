namespace Tnzi.EFCore.Tests;

/// <summary>
/// 经变更跟踪器新增的实体，Id 由框架的生成规则给出（注册了 <see cref="IEntityIdGenerator"/> 就用它），
/// 而不是 EF 在 Add 那一刻按约定塞进去的随机 Guid。
/// </summary>
/// <remarks>
/// ★★ EF 的约定把单列 Guid 主键配成「新增时客户端生成」，<c>DbSet.Add</c> 的那一刻就填上一个 Guid。
/// <c>SaveChanges</c> 里的自动赋 Id 只在 Id 仍为默认值时才动手，于是对 Guid 主键它从来轮不到：
/// 注册的 <see cref="IEntityIdGenerator"/> 被静默无视，按 provider 选的顺序 Guid 排列也从未生效，
/// 只有 <c>NewId()</c> 与 Dapper 批量插入两条路用上了框架的规则。
/// </remarks>
public class EntityIdGeneratorAppliesTests : IDisposable
{
    private static readonly Guid GeneratedId = Guid.Parse("0f8b6a4e-3c2d-4e1f-9a7b-5c6d7e8f9a0b");

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public EntityIdGeneratorAppliesTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<ICurrentTenant>(new MockCurrentTenant());
        services.AddSingleton<IEntityIdGenerator>(new FixedIdGenerator(GeneratedId));
        services.AddDbContext<TestDbContext>(o => o.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task AddedGuidEntity_GetsItsIdFromTheRegisteredGenerator()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var entity = new TestEntityWithGuidId { Name = "generated" };

        context.TestEntitiesWithGuidId.Add(entity);
        await context.SaveChangesAsync();

        Assert.Equal(GeneratedId, entity.Id);
    }

    [Fact]
    public async Task ExplicitId_IsKept()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var manual = Guid.NewGuid();
        var entity = new TestEntityWithGuidId { Id = manual, Name = "manual" };

        context.TestEntitiesWithGuidId.Add(entity);
        await context.SaveChangesAsync();

        Assert.Equal(manual, entity.Id);
    }

    private sealed class FixedIdGenerator(Guid id) : IEntityIdGenerator
    {
        public object? GenerateId(Type entityType, Type idType) => idType == typeof(Guid) ? id : null;
    }
}
