namespace Tnzi.Identity.Organization.Tests.Integration;

using Tnzi.Identity.Organization.Entities;

/// <summary>
/// 组织树与成员关系在写入后仍然自洽：删除组织摘下它的成员、移动 / 新建不接受自己或找不到的父组织。
/// 真实 SQLite，用户表与组织表在同一个上下文里。
/// </summary>
public class OrganizationTreeIntegrityTests : OrganizationIntegrationTestBase
{
    private async Task<Organization> SeedOrganizationAsync(string name, Organization? parent = null)
    {
        var id = Guid.NewGuid();
        var org = new Organization
        {
            Id = id,
            Name = name,
            ParentId = parent?.Id,
            Path = $"{parent?.Path ?? "/"}{id}/",
            Level = (parent?.Level ?? 0) + 1,
            IsEnabled = true,
            CreationTime = DateTime.UtcNow,
        };
        DbContext.Set<Organization>().Add(org);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return org;
    }

    private async Task<User> SeedUserAsync(Guid? organizationId, bool deleted = false)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = $"u_{Guid.NewGuid():N}",
            NormalizedUserName = $"U_{Guid.NewGuid():N}",
            OrganizationId = organizationId,
            IsDeleted = deleted,
            CreationTime = DateTime.UtcNow,
        };
        DbContext.Set<User>().Add(user);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return user;
    }

    private Guid? OrganizationOf(Guid userId)
    {
        DbContext.ChangeTracker.Clear();
        return DbContext.Set<User>().IgnoreQueryFilters().Single(u => u.Id == userId).OrganizationId;
    }

    // ── 删除：成员被摘下 ──────────────────────────────────────────────────────

    /// <summary>
    /// ★ 组织是软删的，外键上的 SetNull 从不触发：此前成员仍指着已删除的组织，
    /// 从用户列表编辑他们会把那个 id 带回去、被「组织不存在」拒绝。
    /// </summary>
    [Fact]
    public async Task Deleting_an_organization_detaches_its_members()
    {
        var sales = await SeedOrganizationAsync("Sales");
        var other = await SeedOrganizationAsync("Other");
        var member = await SeedUserAsync(sales.Id);
        var formerMember = await SeedUserAsync(sales.Id, deleted: true);
        var bystander = await SeedUserAsync(other.Id);

        var result = await Service.DeleteAsync(sales.Id);

        result.Succeeded.ShouldBeTrue(result.Message);
        OrganizationOf(member.Id).ShouldBeNull();
        OrganizationOf(formerMember.Id).ShouldBeNull();
        OrganizationOf(bystander.Id).ShouldBe(other.Id);
    }

    [Fact]
    public async Task Deleting_several_organizations_detaches_all_their_members()
    {
        var a = await SeedOrganizationAsync("A");
        var b = await SeedOrganizationAsync("B");
        var inA = await SeedUserAsync(a.Id);
        var inB = await SeedUserAsync(b.Id);

        (await Service.DeleteManyAsync([a.Id, b.Id])).Succeeded.ShouldBeTrue();

        OrganizationOf(inA.Id).ShouldBeNull();
        OrganizationOf(inB.Id).ShouldBeNull();
    }

    [Fact]
    public async Task An_organization_with_children_is_still_not_deleted_and_keeps_its_members()
    {
        var parent = await SeedOrganizationAsync("Parent");
        await SeedOrganizationAsync("Child", parent);
        var member = await SeedUserAsync(parent.Id);

        (await Service.DeleteAsync(parent.Id)).Code.ShouldBe(400);
        OrganizationOf(member.Id).ShouldBe(parent.Id);
    }

    // ── 移动 / 新建：父组织必须是别的、存在的组织 ─────────────────────────────

    /// <summary>
    /// ★ 后代集合不含自己，此前 newParentId == id 放行：组织成了自己的父节点，连同子树从组织树里消失。
    /// </summary>
    [Fact]
    public async Task Moving_an_organization_under_itself_is_refused_and_the_tree_is_unchanged()
    {
        var root = await SeedOrganizationAsync("Root");
        await SeedOrganizationAsync("Child", root);

        var result = await Service.MoveAsync(root.Id, root.Id);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        var tree = (await Service.GetTreeAsync()).Data!.ToList();
        tree.Select(n => n.Id).ShouldContain(root.Id);
        tree.Single(n => n.Id == root.Id).Children.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Moving_under_a_parent_that_does_not_exist_is_refused()
    {
        var org = await SeedOrganizationAsync("Org");

        var result = await Service.MoveAsync(org.Id, Guid.NewGuid());

        result.Code.ShouldBe(404);
        DbContext.ChangeTracker.Clear();
        var reloaded = DbContext.Set<Organization>().Single(o => o.Id == org.Id);
        reloaded.ParentId.ShouldBeNull();
        reloaded.Path.ShouldBe(org.Path);
    }

    [Fact]
    public async Task Creating_under_a_parent_that_does_not_exist_is_refused()
    {
        var result = await Service.CreateAsync(new CreateOrganizationDto { Name = "Orphan", ParentId = Guid.NewGuid() });

        result.Code.ShouldBe(404);
        DbContext.Set<Organization>().Any(o => o.Name == "Orphan").ShouldBeFalse();
    }

    [Fact]
    public async Task Creating_several_under_a_parent_that_does_not_exist_is_refused()
    {
        var existing = await SeedOrganizationAsync("Existing");

        var result = await Service.CreateManyAsync(
        [
            new CreateOrganizationDto { Name = "Fine", ParentId = existing.Id },
            new CreateOrganizationDto { Name = "Orphan", ParentId = Guid.NewGuid() },
        ]);

        result.Code.ShouldBe(404);
        DbContext.Set<Organization>().Any(o => o.Name == "Fine" || o.Name == "Orphan").ShouldBeFalse();
    }
}
