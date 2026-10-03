namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 退订登记的并发判重，以及管理端按传真号码片段查找。
/// </summary>
public class OptOutRaceAndLookupTests : IntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<OptOut, Guid>>(sp =>
            new RacingOptOutRepository(sp.GetRequiredService<NotificationTestDbContext>(), sp));

        var options = new Mock<IOptionsMonitor<NotificationOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new NotificationOptions());
        services.AddSingleton(_ => options.Object);

        services.AddScoped<INotificationOptOutService, NotificationOptOutService>();
    }

    private INotificationOptOutService Service => ServiceProvider.GetRequiredService<INotificationOptOutService>();

    private RacingOptOutRepository Repository => (RacingOptOutRepository)ServiceProvider.GetRequiredService<IRepository<OptOut, Guid>>();

    /// <summary>
    /// ★ 判重读到「还没有」之后、插入之前，另一个请求登记了同一条：唯一索引挡下第二次插入。
    /// 那一次表达的是已经成立的事实，必须成功并交回已有那一行，而不是 500；
    /// 失败的实体也不能留在变更跟踪器里，否则同一作用域下一次保存会把它重放一遍。
    /// </summary>
    [Fact]
    public async Task Register_LosingTheRace_ReturnsTheExistingRow_AndLeavesNothingToReplay()
    {
        Repository.CompetingRow = new OptOut
        {
            Address = "racer@example.com",
            Channel = NotificationType.Email,
            Source = "one-click link",
        };

        var result = await Service.RegisterAsync(new CreateOptOutDto { Address = "Racer@example.com", Channel = NotificationType.Email });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Source.ShouldBe("one-click link", "the row that won the race is the one on record");
        (await DbContext.OptOuts.AsNoTracking().CountAsync()).ShouldBe(1);

        // 同一作用域的下一次保存不应重放那次失败的插入。
        await Should.NotThrowAsync(() => DbContext.SaveChangesAsync());
    }

    /// <summary>一键退订走同一条路径：收件人连点两下，两次都是「已退订」。</summary>
    [Fact]
    public async Task OptOut_LosingTheRace_Succeeds()
    {
        Repository.CompetingRow = new OptOut
        {
            Address = "double@example.com",
            Channel = NotificationType.Email,
            Category = "marketing",
        };

        var result = await Service.OptOutAsync("double@example.com", NotificationType.Email, "marketing", "one-click link");

        result.Succeeded.ShouldBeTrue(result.Message);
        (await DbContext.OptOuts.AsNoTracking().CountAsync()).ShouldBe(1);
        await Should.NotThrowAsync(() => DbContext.SaveChangesAsync());
    }

    /// <summary>
    /// ★ 不够一个完整号码、又带着括号空格的片段：登记存的是纯数字，片段要剥成数字再做包含匹配，
    /// 否则 <c>(905) 555</c> 永远命中不了 <c>9055551234</c>。
    /// </summary>
    [Theory]
    [InlineData("(905) 555")]
    [InlineData("905-555")]
    [InlineData("555 12")]
    public async Task GetPagedList_ByFaxChannel_MatchesAPunctuatedFragment(string fragment)
    {
        DbContext.OptOuts.Add(new OptOut { Id = Guid.NewGuid(), Address = "9055551234", Channel = NotificationType.Fax });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var page = await Service.GetPagedListAsync(new OptOutQueryDto { Channel = NotificationType.Fax, Address = fragment });

        page.Data!.Items.ShouldHaveSingleItem().Address.ShouldBe("9055551234");
    }

    /// <summary>夹着字母的片段不是号码片段，原样匹配（不会被剥成一串数字误命中）。</summary>
    [Fact]
    public async Task GetPagedList_ByFaxChannel_ANonNumberFragment_IsMatchedAsWritten()
    {
        DbContext.OptOuts.Add(new OptOut { Id = Guid.NewGuid(), Address = "9055551234", Channel = NotificationType.Fax });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var page = await Service.GetPagedListAsync(new OptOutQueryDto { Channel = NotificationType.Fax, Address = "905 ext 555" });

        page.Data!.Items.ShouldBeEmpty();
    }

    /// <summary>
    /// 在服务的判重之后、它自己的插入之前，经另一个 DbContext 提交一条相同的退订 —— 与并发请求的时序相同。
    /// </summary>
    private sealed class RacingOptOutRepository(NotificationTestDbContext dbContext, IServiceProvider serviceProvider)
        : EFCoreRepository<NotificationTestDbContext, OptOut, Guid>(dbContext, serviceProvider: serviceProvider)
    {
        private readonly IServiceProvider _root = serviceProvider;

        public OptOut? CompetingRow { get; set; }

        public override async Task InsertAsync(OptOut entity, CancellationToken cancellationToken = default)
        {
            if (CompetingRow != null)
            {
                using var scope = _root.CreateScope();
                var other = scope.ServiceProvider.GetRequiredService<NotificationTestDbContext>();
                other.OptOuts.Add(CompetingRow);
                await other.SaveChangesAsync(cancellationToken);
                CompetingRow = null;
            }

            await base.InsertAsync(entity, cancellationToken);
        }
    }
}
