namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 退订原因是附注，退订本身是必须生效的动作：附注再长也不能让退订失败。
/// </summary>
/// <remarks>
/// <b>被保护的缺陷</b>：匿名退订端点的 <c>Reason</c> 没有长度限制，服务原样存进 <c>varchar(1000)</c>。
/// 超长时 PostgreSQL 拒绝插入（不是唯一冲突，没人接），端点答 500、退订没记下，这个地址照样收信。
/// SQLite 不检查列宽，所以断言对着 EF 模型声明的列宽，而不是等库来拒绝。
/// </remarks>
public class OptOutReasonLengthTests : IntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<OptOut, Guid>>(sp =>
            new EFCoreRepository<NotificationTestDbContext, OptOut, Guid>(
                sp.GetRequiredService<NotificationTestDbContext>(), serviceProvider: sp));

        var options = new Mock<IOptionsMonitor<NotificationOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new NotificationOptions());
        services.AddSingleton(_ => options.Object);

        services.AddScoped<INotificationOptOutService, NotificationOptOutService>();
    }

    private INotificationOptOutService Service => ServiceProvider.GetRequiredService<INotificationOptOutService>();

    [Fact]
    public async Task AnOverlongReason_StillRecordsTheOptOut_WithTheReasonCutToTheColumn()
    {
        var result = await Service.OptOutAsync("alice@example.com", NotificationType.Email, reason: new string('x', 5000));

        result.Succeeded.ShouldBeTrue(result.Message);
        DbContext.ChangeTracker.Clear();
        var row = await DbContext.OptOuts.AsNoTracking().SingleAsync();
        var maxLength = DbContext.Model.FindEntityType(typeof(OptOut))!.FindProperty(nameof(OptOut.Reason))!.GetMaxLength();
        maxLength.ShouldNotBeNull();
        row.Reason.ShouldNotBeNull();
        row.Reason!.Length.ShouldBeLessThanOrEqualTo(maxLength!.Value);
        row.Reason.ShouldEndWith("…", customMessage: "截断要看得出来，否则读的人以为那是全文");
    }

    [Fact]
    public async Task AnOverlongSource_IsCutToTheColumnToo()
    {
        await Service.OptOutAsync("bob@example.com", NotificationType.Email, source: new string('s', 900));

        DbContext.ChangeTracker.Clear();
        var row = await DbContext.OptOuts.AsNoTracking().SingleAsync();
        var maxLength = DbContext.Model.FindEntityType(typeof(OptOut))!.FindProperty(nameof(OptOut.Source))!.GetMaxLength();
        row.Source!.Length.ShouldBeLessThanOrEqualTo(maxLength!.Value);
    }

    [Fact]
    public async Task AShortReason_IsKeptAsWritten_AndABlankOneIsDropped()
    {
        await Service.OptOutAsync("carol@example.com", NotificationType.Email, reason: "Too many emails");
        await Service.OptOutAsync("dave@example.com", NotificationType.Email, reason: "   ");

        DbContext.ChangeTracker.Clear();
        var rows = await DbContext.OptOuts.AsNoTracking().ToDictionaryAsync(o => o.Address, o => o.Reason);
        rows["carol@example.com"].ShouldBe("Too many emails");
        rows["dave@example.com"].ShouldBeNull();
    }
}
