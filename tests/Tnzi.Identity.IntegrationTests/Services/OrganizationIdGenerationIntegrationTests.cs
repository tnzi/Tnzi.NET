using Tnzi.EFCore.Data;
using Tnzi.Identity.Organization.Services;
using Tnzi.MultiTenancy;

namespace Tnzi.Identity.IntegrationTests.Services;

// 见 IntegrationTestBase.cs 顶部：`Organization` 的 using 必须在命名空间体内。
using Tnzi.Identity.Organization.Entities;

/// <summary>
/// 组织的 Id 要在拼 <c>Path</c> 之前定下来（路径段就是实体 Id），于是它绕过了 SaveChanges 的自动生成。
/// ★ 预先定下的那枚 Id 必须走<b>同一套规则</b>：注册了 <see cref="IEntityIdGenerator"/> 就交给它，
/// 否则按数据库 provider 选 Sequential GUID 的排列 —— 此前这里直接 <c>SequentialGuid.NewGuid()</c>，
/// 无论 provider 恒为 SQL Server 的排列，消费方注册的生成器也被绕过，组织表是全仓唯一一张按别的规则发 Id 的表。
/// </summary>
public class OrganizationIdGenerationIntegrationTests : RelationalIdentityIntegrationTestBase
{
    /// <summary>发出的每枚 Guid 前 4 个字节都是 0xAB，一眼认得出是它发的。</summary>
    private sealed class MarkedIdGenerator : IEntityIdGenerator
    {
        public static bool IsMarked(Guid id) => id.ToByteArray().Take(4).All(b => b == 0xAB);

        public object? GenerateId(Type entityType, Type idType)
        {
            if (idType != typeof(Guid))
            {
                return null;
            }

            var bytes = Guid.NewGuid().ToByteArray();
            bytes[0] = bytes[1] = bytes[2] = bytes[3] = 0xAB;
            return new Guid(bytes);
        }
    }

    private readonly OrganizationService _service;

    public OrganizationIdGenerationIntegrationTests()
        : base(configureServices: services => services.AddSingleton<IEntityIdGenerator, MarkedIdGenerator>())
    {
        _service = new OrganizationService(
            CreateRepository<Organization>(),
            ServiceProvider,
            DbContext,
            eventBus: EventBusMock.Object,
            currentUser: ServiceProvider.GetRequiredService<ICurrentUser>(),
            currentTenant: null,
            multiTenancyOptions: Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions()),
            cache: Cache,
            userManager: UserManager);
    }

    [Fact]
    public async Task Create_And_CreateMany_UseTheRegisteredIdGenerator_AndPathEndsWithThatId()
    {
        var root = (await _service.CreateAsync(new CreateOrganizationDto { Name = "Root" })).Data!;
        var batch = (await _service.CreateManyAsync([
            new CreateOrganizationDto { Name = "B1", ParentId = root.Id },
        ])).Data!.Single();

        DbContext.ChangeTracker.Clear();
        var rows = await DbContext.Organizations.ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.True(MarkedIdGenerator.IsMarked(row.Id), $"{row.Name} got an id from outside the registered generator: {row.Id}");
            Assert.EndsWith($"/{row.Id}/", row.Path);
        });
        Assert.True(MarkedIdGenerator.IsMarked(batch.Id));
    }

    /// <summary>仓储的 <c>NewId()</c> 与 SaveChanges 同源：注册了生成器就出自它。</summary>
    [Fact]
    public void Repository_NewId_HonoursTheRegisteredGenerator()
    {
        var id = CreateRepository<Organization>().NewId();

        Assert.NotEqual(Guid.Empty, id);
        Assert.True(MarkedIdGenerator.IsMarked(id));
    }
}
