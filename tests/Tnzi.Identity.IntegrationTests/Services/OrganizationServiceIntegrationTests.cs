using Microsoft.Extensions.Options;
using Tnzi.Identity.Services;
using Tnzi.MultiTenancy;

namespace Tnzi.Identity.IntegrationTests.Services;

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
}
