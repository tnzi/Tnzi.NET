using Tnzi.MultiTenancy;

namespace Tnzi.System.Tests.Integration;

/// <summary>
/// Top 端点统计：分组之后的排序与取前 N 在数据库端完成，<c>top</c> 夹在 1 到 <see cref="AccessLogService.MaxTopEndpoints"/> 之间。
/// </summary>
/// <remarks>
/// Path 是原始请求路径（带资源 id），分组数约等于不同 URL 数。此前全部分组物化进内存再排序，
/// 开着采集几周之后一次不带时间范围的调用要把几十万个分组拉进 API 进程，只为返回 10 行；<c>top</c> 也没有上下界。
/// 这一组在真实的 SQLite 上跑，守住「排序与取前 N 翻译得成 SQL」与结果本身。
/// </remarks>
public class AccessLogTopEndpointsTests : IntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    private AccessLogService Service() => new(
        ServiceProvider,
        new EFCoreRepository<SystemTestDbContext, AccessLog, Guid>(DbContext, serviceProvider: ServiceProvider),
        new Mock<IAccessLogSender>().Object,
        accessLogOptions: null,
        currentTenant: null,
        multiTenancyOptions: Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = false }));

    private void Seed(params (string Path, int StatusCode, long ResponseTime)[] hits)
    {
        var now = DateTime.UtcNow;
        DbContext.AccessLogs.AddRange(hits.Select(h => new AccessLog
        {
            Id = Guid.NewGuid(),
            Path = h.Path,
            Method = "GET",
            StatusCode = h.StatusCode,
            ResponseTime = h.ResponseTime,
            CreationTime = now.AddMinutes(-1),
        }));
        DbContext.SaveChangesAsync().GetAwaiter().GetResult();
        DbContext.ChangeTracker.Clear();
    }

    private void SeedTypicalTraffic() => Seed(
        ("/api/hot", 200, 10), ("/api/hot", 200, 12), ("/api/hot", 200, 14), ("/api/hot", 500, 16),
        ("/api/slow", 200, 900), ("/api/slow", 200, 1100),
        ("/api/broken", 500, 20), ("/api/broken", 404, 20), ("/api/broken", 500, 20),
        ("/api/quiet", 200, 5));

    [Fact]
    public async Task By_hits_returns_the_busiest_endpoints_first_with_their_statistics()
    {
        SeedTypicalTraffic();

        var result = await Service().GetTopEndpointsAsync(top: 2, sortBy: TopEndpointSortBy.Hits);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Select(e => e.Path).ShouldBe(["/api/hot", "/api/broken"]);
        var hot = result.Data![0];
        hot.TotalHits.ShouldBe(4);
        hot.SuccessHits.ShouldBe(3);
        hot.ErrorHits.ShouldBe(1);
        hot.ErrorRate.ShouldBe(0.25);
        hot.AverageResponseTime.ShouldBe(13);
        hot.MaxResponseTime.ShouldBe(16);
    }

    [Fact]
    public async Task By_average_response_time_returns_the_slowest_first()
    {
        SeedTypicalTraffic();

        var result = await Service().GetTopEndpointsAsync(top: 1, sortBy: TopEndpointSortBy.AverageResponseTime);

        result.Data!.ShouldHaveSingleItem().Path.ShouldBe("/api/slow");
    }

    [Fact]
    public async Task By_errors_returns_the_most_failing_first()
    {
        SeedTypicalTraffic();

        var result = await Service().GetTopEndpointsAsync(top: 1, sortBy: TopEndpointSortBy.Errors);

        result.Data!.ShouldHaveSingleItem().Path.ShouldBe("/api/broken");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_top_below_one_still_returns_the_first_endpoint(int top)
    {
        SeedTypicalTraffic();

        var result = await Service().GetTopEndpointsAsync(top: top);

        result.Data!.ShouldHaveSingleItem().Path.ShouldBe("/api/hot");
    }

    [Fact]
    public async Task A_top_above_the_cap_is_clamped()
    {
        Seed(Enumerable.Range(0, AccessLogService.MaxTopEndpoints + 5).Select(i => ($"/api/files/{i}/preview", 200, 5L)).ToArray());

        var result = await Service().GetTopEndpointsAsync(top: 10_000);

        result.Data!.Count.ShouldBe(AccessLogService.MaxTopEndpoints);
    }
}
