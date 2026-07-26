namespace Tnzi.AI.Tests.Services;

/// <summary>
/// QuotaService 单元测试 - 覆盖 GetPagedListAsync 分页查询
/// </summary>
public class QuotaServiceTests
{
    private readonly Mock<IRepository<UserQuota, Guid>> _repository = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<AIOptions> _options;

    public QuotaServiceTests()
    {
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        _options = new StaticOptionsMonitor<AIOptions>(new AIOptions());
    }

    private QuotaService CreateService() => new(_repository.Object, _options, _serviceProvider);

    private void SetupQueryable(List<UserQuota> data)
    {
        var mock = data.BuildMock();
        _repository.As<IQueryable<UserQuota>>().Setup(q => q.Provider).Returns(mock.Provider);
        _repository.As<IQueryable<UserQuota>>().Setup(q => q.Expression).Returns(mock.Expression);
        _repository.As<IQueryable<UserQuota>>().Setup(q => q.ElementType).Returns(mock.ElementType);
        _repository.As<IQueryable<UserQuota>>().Setup(q => q.GetEnumerator()).Returns(mock.GetEnumerator());
    }

    private static UserQuota MakeQuota(Guid? userId = null, bool isEnabled = true, long dailyLimit = 100000, long monthlyLimit = 3000000) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId ?? Guid.NewGuid(),
        DailyTokenLimit = dailyLimit,
        MonthlyTokenLimit = monthlyLimit,
        CurrentDailyUsage = 0,
        CurrentMonthlyUsage = 0,
        LastResetDate = DateTime.UtcNow,
        IsEnabled = isEnabled,
        WarningThreshold = 0.8m,
        CriticalThreshold = 0.95m,
        CreationTime = DateTime.UtcNow
    };

    [Fact]
    public async Task GetPagedListAsync_EmptyRepository_ReturnsEmptyPagedList()
    {
        SetupQueryable(new List<UserQuota>());
        var svc = CreateService();

        var result = await svc.GetPagedListAsync(new UserQuotaQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Items.Count.ShouldBe(0);
        result.Data.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetPagedListAsync_TwoUsers_ReturnsBoth()
    {
        var quotas = new List<UserQuota>
        {
            MakeQuota(),
            MakeQuota()
        };
        SetupQueryable(quotas);
        var svc = CreateService();

        var result = await svc.GetPagedListAsync(new UserQuotaQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Items.Count.ShouldBe(2);
        result.Data.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetPagedListAsync_FilterByUserId_ReturnsMatching()
    {
        var targetUserId = Guid.NewGuid();
        var quotas = new List<UserQuota>
        {
            MakeQuota(userId: targetUserId),
            MakeQuota()
        };
        SetupQueryable(quotas);
        var svc = CreateService();

        var result = await svc.GetPagedListAsync(new UserQuotaQueryDto { UserId = targetUserId, PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Items.Count.ShouldBe(1);
        result.Data.Items[0].UserId.ShouldBe(targetUserId);
    }
}
