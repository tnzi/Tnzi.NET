namespace Tnzi.Identity.Organization.Tests.Integration;

using Tnzi.Identity.Organization.Entities;

/// <summary>
/// 组织联想搜索的条数有上界：它服务的是选择器，不是导出。
/// </summary>
public class OrganizationSearchLimitTests : OrganizationIntegrationTestBase
{
    private const int Seeded = 130;

    private async Task SeedAsync()
    {
        for (var i = 0; i < Seeded; i++)
        {
            DbContext.Set<Organization>().Add(new Organization
            {
                Id = Guid.NewGuid(),
                Name = $"Team {i:D3}",
                Code = $"T{i:D3}",
                Path = $"/{Guid.NewGuid()}/",
                CreationTime = DateTime.UtcNow,
            });
        }

        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    /// <summary>★ 一个极大的 maxResults 加一个人人命中的关键字，不会把整张组织表交出去。</summary>
    [Fact]
    public async Task AHugeMaxResults_IsCappedAtOneHundred()
    {
        await SeedAsync();

        var result = await Service.SearchAsync("team", int.MaxValue);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Count().ShouldBe(100);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ANonPositiveMaxResults_FallsBackToTheDefault(int maxResults)
    {
        await SeedAsync();

        var result = await Service.SearchAsync("team", maxResults);

        result.Data!.Count().ShouldBe(20);
    }
}
