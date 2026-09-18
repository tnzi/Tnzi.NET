using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.MultiTenancy;
using Tnzi.Identity.Organization.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

// 见 IntegrationTestBase.cs 顶部：`Organization` 的 using 必须在命名空间体内。
using Tnzi.Identity.Organization.Entities;

public class OrganizationServiceIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private readonly OrganizationService _service;

    public OrganizationServiceIntegrationTests()
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
    public async Task GetTreeAsync_ReturnsOrganizationTree()
    {
        var root = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Root",
            Path = $"/{Guid.NewGuid()}/",
            Level = 1,
            SortOrder = 1,
            IsEnabled = true
        };
        var child = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Child",
            ParentId = root.Id,
            Path = $"{root.Path}{Guid.NewGuid()}/",
            Level = 2,
            SortOrder = 1,
            IsEnabled = true
        };

        DbContext.Organizations.AddRange(root, child);
        await SaveChangesAsync();

        var result = await _service.GetTreeAsync();

        Assert.True(result.Succeeded);
        var tree = result.Data!.ToList();
        Assert.Single(tree);
        Assert.Equal("Root", tree[0].Name);
        Assert.Single(tree[0].Children);
        Assert.Equal("Child", tree[0].Children[0].Name);
    }

    [Fact]
    public async Task MoveAsync_WithValidInput_MovesOrganization()
    {
        var root = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Root",
            Path = "/",
            Level = 1,
            SortOrder = 1
        };
        root.Path = $"/{root.Id}/";

        var child = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Child",
            ParentId = root.Id,
            Path = $"{root.Path}{Guid.NewGuid()}/",
            Level = 2,
            SortOrder = 1
        };

        var newParent = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "NewParent",
            Path = $"/{Guid.NewGuid()}/",
            Level = 1,
            SortOrder = 2
        };

        DbContext.Organizations.AddRange(root, child, newParent);
        await SaveChangesAsync();
        var oldPath = child.Path!;

        var result = await _service.MoveAsync(child.Id, newParent.Id);

        Assert.True(result.Succeeded);
        var reloaded = await DbContext.Organizations.FindAsync(child.Id);
        Assert.Equal(newParent.Id, reloaded!.ParentId);
        Assert.NotEqual(oldPath, reloaded.Path);
        Assert.StartsWith(newParent.Path!, reloaded.Path);
    }

    /// <summary>
    /// ★ 经服务建出来的层级（不许手工塞 Path），祖先查询要能回答。此前 CreateAsync 往 Path 末段写的是
    /// 一枚随机 GUID 而不是实体自己的 Id，GetAllParentsAsync 却把路径段当祖先 Id 解析 ——
    /// 于是 GET admin/organizations/{id}/parents 自初始提交起恒返回空数组。
    /// </summary>
    [Fact]
    public async Task Create_Parent_And_Child_ViaService_GetAllParents_ReturnsTheChain()
    {
        var root = (await _service.CreateAsync(new CreateOrganizationDto { Name = "Root" })).Data!;
        var mid = (await _service.CreateAsync(new CreateOrganizationDto { Name = "Mid", ParentId = root.Id })).Data!;
        var leaf = (await _service.CreateAsync(new CreateOrganizationDto { Name = "Leaf", ParentId = mid.Id })).Data!;

        var parents = (await _service.GetAllParentsAsync(leaf.Id)).Data!.Select(p => p.Id).ToList();

        Assert.Equal(new[] { root.Id, mid.Id }, parents);
    }

    /// <summary>Path 的每一段都是实体 Id：单条创建、批量创建、移动之后都成立，三条写路径一个口径。</summary>
    [Fact]
    public async Task Create_CreateMany_And_Move_WritePathSegmentsThatAreEntityIds()
    {
        var root = (await _service.CreateAsync(new CreateOrganizationDto { Name = "Root" })).Data!;
        var batch = (await _service.CreateManyAsync([
            new CreateOrganizationDto { Name = "B1", ParentId = root.Id },
            new CreateOrganizationDto { Name = "B2" },
        ])).Data!.ToList();
        var b1 = batch.Single(o => o.Name == "B1");
        var b2 = batch.Single(o => o.Name == "B2");
        Assert.True((await _service.MoveAsync(b1.Id, b2.Id)).Succeeded);

        DbContext.ChangeTracker.Clear();
        var rows = await DbContext.Organizations.ToListAsync();

        Assert.Equal($"/{root.Id}/", rows.Single(o => o.Id == root.Id).Path);
        Assert.Equal($"/{b2.Id}/", rows.Single(o => o.Id == b2.Id).Path);
        Assert.Equal($"/{b2.Id}/{b1.Id}/", rows.Single(o => o.Id == b1.Id).Path);
    }

    /// <summary>
    /// 存量修复：旧写法留下的「随机 GUID 路径段」按 ParentId 链重建；已经正确的行不动；第二遍零改写（幂等）。
    /// </summary>
    [Fact]
    public async Task PathRepair_RebuildsLegacyRandomSegments_AndIsIdempotent()
    {
        var root = new Organization { Id = Guid.NewGuid(), Name = "Root", Path = $"/{Guid.NewGuid()}/", Level = 1 };
        var child = new Organization { Id = Guid.NewGuid(), Name = "Child", ParentId = root.Id, Path = $"{root.Path}{Guid.NewGuid()}/", Level = 2 };
        var fine = new Organization { Id = Guid.NewGuid(), Name = "Fine", Level = 1 };
        fine.Path = $"/{fine.Id}/";
        DbContext.Organizations.AddRange(root, child, fine);
        await SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var repaired = await OrganizationPathRepairStartupTask.RepairAsync(
            CreateRepository<Organization>(), Cache, multiTenancyEnabled: false, NullLogger.Instance);
        DbContext.ChangeTracker.Clear();
        var again = await OrganizationPathRepairStartupTask.RepairAsync(
            CreateRepository<Organization>(), Cache, multiTenancyEnabled: false, NullLogger.Instance);

        Assert.Equal(2, repaired);
        Assert.Equal(0, again);
        var rows = await DbContext.Organizations.ToListAsync();
        Assert.Equal($"/{root.Id}/", rows.Single(o => o.Id == root.Id).Path);
        Assert.Equal($"/{root.Id}/{child.Id}/", rows.Single(o => o.Id == child.Id).Path);
        Assert.Equal((await _service.GetAllParentsAsync(child.Id)).Data!.Single().Id, root.Id);
    }
}
