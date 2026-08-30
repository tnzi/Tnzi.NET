namespace Tnzi.Identity.Organization.Tests.Integration;

using Tnzi.Identity.Organization.Entities;

/// <summary>
/// 加载本包时，「用户 → 组织名」这一列仍然填得出来。
/// </summary>
/// <remarks>
/// 拆分前它来自 <c>User → Organization</c> 的 LEFT JOIN；导航属性删掉之后，改由
/// <c>IOrganizationService.GetNamesAsync</c> 按整页批量翻译。这里证明"加载 = 有名字"，
/// <c>Tnzi.Identity.Tests/OrganizationPackageAbsenceTests</c> 证明"不加载 = 留空且不报错"。
/// 两边合起来才说清这个契约的语义：**只少一列显示值，不改任何其它行为**。
/// </remarks>
public class OrganizationNameLookupTests : OrganizationIntegrationTestBase
{
    private async Task<Organization> SeedAsync(string name, string code)
    {
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = name,
            Code = code,
            Path = $"/{Guid.NewGuid()}/",
            CreationTime = DateTime.UtcNow,
        };
        DbContext.Set<Organization>().Add(org);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return org;
    }

    private async Task<User> SeedUserAsync(Guid? organizationId)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = $"u_{Guid.NewGuid():N}",
            NormalizedUserName = $"U_{Guid.NewGuid():N}",
            OrganizationId = organizationId,
            CreationTime = DateTime.UtcNow,
        };
        DbContext.Set<User>().Add(user);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return user;
    }

    [Fact]
    public async Task GetNamesAsync_TranslatesTheIdsItKnows()
    {
        var a = await SeedAsync("Engineering", "ENG");
        var b = await SeedAsync("Finance", "FIN");
        var unknown = Guid.NewGuid();

        var names = await Service.GetNamesAsync([a.Id, b.Id, unknown, a.Id]);

        names[a.Id].ShouldBe("Engineering");
        names[b.Id].ShouldBe("Finance");
        // 查不到的 Id 不出现在结果里 —— 挂在已删组织上的用户不是错误，名字留空即可。
        names.ContainsKey(unknown).ShouldBeFalse();
    }

    [Fact]
    public async Task GetNamesAsync_WithNoIds_DoesNotQuery()
    {
        (await Service.GetNamesAsync([])).ShouldBeEmpty();
    }

    /// <summary>
    /// 组织下的用户列表仍然带着组织名 —— 这是拆分前 <c>ProjectTo</c> 直接投影出来的那一列。
    /// </summary>
    [Fact]
    public async Task GetUsersAsync_FillsTheOrganizationName()
    {
        var org = await SeedAsync("Engineering", "ENG2");
        await SeedUserAsync(org.Id);

        var result = await Service.GetUsersAsync(org.Id, new PagedQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Items.Count.ShouldBe(1);
        result.Data.Items.Single().OrganizationName.ShouldBe("Engineering");
    }
}
