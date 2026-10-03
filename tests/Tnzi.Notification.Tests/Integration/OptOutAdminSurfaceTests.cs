namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 退订名单的管理面：分页查、手工登记、按 id 撤销 —— 真库、真 <see cref="NotificationOptOutService"/>。
/// </summary>
/// <remarks>
/// <para>
/// 在这之前 <c>OptOut</c> 表只有写入面（一键链接 / 发送路径的过滤查询），没有任何读取面：
/// 合规问询「这个地址何时经哪个渠道退订」、客户来电「我误点了请恢复」、服务商转来的投诉要人工补录，
/// 一个都办不了 —— 登记完拿不出来等于没登记。
/// </para>
/// </remarks>
public class OptOutAdminSurfaceTests : IntegrationTestBase
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

    private static readonly DateTime Base = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private async Task SeedAsync(string address, NotificationType channel, string? category, DateTime when, string? source = "one-click link")
    {
        DbContext.OptOuts.Add(new OptOut
        {
            Id = Guid.NewGuid(),
            Address = address,
            Channel = channel,
            Category = category,
            Source = source,
            CreationTime = when,
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task GetPagedList_FiltersByAddressChannelCategoryAndWindow()
    {
        await SeedAsync("alice@example.com", NotificationType.Email, "marketing", Base.AddDays(-10));
        await SeedAsync("alice@example.com", NotificationType.Sms, null, Base.AddDays(-5));
        await SeedAsync("bob@example.com", NotificationType.Email, null, Base.AddDays(-1));

        // 地址包含匹配，操作者敲的大小写与空白不该让他查不到（库里存的是归一化后的写法）。
        var byAddress = await Service.GetPagedListAsync(new OptOutQueryDto { Address = "  ALICE  " });
        byAddress.Data!.Items.Select(o => o.Address).ShouldBe(["alice@example.com", "alice@example.com"]);

        var byChannel = await Service.GetPagedListAsync(new OptOutQueryDto { Channel = NotificationType.Email });
        byChannel.Data!.Items.Select(o => o.Address).ShouldBe(["bob@example.com", "alice@example.com"]);

        var byCategory = await Service.GetPagedListAsync(new OptOutQueryDto { Category = "marketing" });
        byCategory.Data!.Items.ShouldHaveSingleItem().Channel.ShouldBe(NotificationType.Email);

        var byWindow = await Service.GetPagedListAsync(new OptOutQueryDto { From = Base.AddDays(-6), To = Base.AddDays(-2) });
        byWindow.Data!.Items.ShouldHaveSingleItem().Channel.ShouldBe(NotificationType.Sms);
    }

    [Fact]
    public async Task GetPagedList_ByFaxChannel_MatchesTheNumberHoweverItIsWritten()
    {
        // 传真登记时归一化成纯数字（见 Normalize）；查的人手里是名片上的写法。
        await SeedAsync("9055551234", NotificationType.Fax, null, Base.AddDays(-1), source: "admin");
        await SeedAsync("+1 (905) 555-1234", NotificationType.Email, null, Base.AddDays(-1));

        var formatted = await Service.GetPagedListAsync(new OptOutQueryDto { Channel = NotificationType.Fax, Address = "+1 (905) 555-1234" });
        formatted.Data!.Items.ShouldHaveSingleItem().Address.ShouldBe("9055551234");

        // 归一化不了的片段退回包含匹配，区号也能查。
        var fragment = await Service.GetPagedListAsync(new OptOutQueryDto { Channel = NotificationType.Fax, Address = "905" });
        fragment.Data!.Items.ShouldHaveSingleItem().Address.ShouldBe("9055551234");

        // 没筛渠道时只去空白加小写：不知道该按哪个渠道的规则归一化。
        var untyped = await Service.GetPagedListAsync(new OptOutQueryDto { Address = "+1 (905) 555-1234" });
        untyped.Data!.Items.ShouldHaveSingleItem().Channel.ShouldBe(NotificationType.Email);
    }

    [Fact]
    public async Task GetPagedList_IsNewestFirstAndPaged()
    {
        for (var i = 0; i < 5; i++)
        {
            await SeedAsync($"user{i}@example.com", NotificationType.Email, null, Base.AddDays(-i));
        }

        var page = await Service.GetPagedListAsync(new OptOutQueryDto { PageIndex = 2, PageSize = 2 });

        page.Data!.TotalCount.ShouldBe(5);
        page.Data.Items.Select(o => o.Address).ShouldBe(["user2@example.com", "user3@example.com"]);
    }

    [Fact]
    public async Task GetPagedList_IncludeChannelWide_ListsOneCategoryTogetherWithTheChannelWideRows()
    {
        // 一张只看「广播」这一档的抑制名单：既要这一档自己的行，也要整渠道退订的人，后者对广播同样生效，
        // 少了这些行，这一页解释不了「这个人为什么收不到」。别的分类与别的渠道的整渠道行都不该混进来。
        await SeedAsync("category@example.com", NotificationType.Email, "broadcast", Base.AddDays(-4));
        await SeedAsync("whole-channel@example.com", NotificationType.Email, null, Base.AddDays(-3));
        await SeedAsync("other@example.com", NotificationType.Email, "marketing", Base.AddDays(-2));
        await SeedAsync("15550001111", NotificationType.Sms, null, Base.AddDays(-1));

        var both = await Service.GetPagedListAsync(new OptOutQueryDto
        {
            Channel = NotificationType.Email,
            Category = "broadcast",
            IncludeChannelWide = true,
        });
        both.Data!.TotalCount.ShouldBe(2);
        both.Data.Items.Select(o => o.Address).ShouldBe(["whole-channel@example.com", "category@example.com"]);

        // 开关关着 = 此前的精确匹配，一字不变。
        var exact = await Service.GetPagedListAsync(new OptOutQueryDto { Channel = NotificationType.Email, Category = "broadcast" });
        exact.Data!.Items.Select(o => o.Address).ShouldBe(["category@example.com"]);

        // 没筛分类时本来就不筛分类，开关不起作用。
        var noCategory = await Service.GetPagedListAsync(new OptOutQueryDto { Channel = NotificationType.Email, IncludeChannelWide = true });
        noCategory.Data!.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task GetPagedList_IncludeChannelWide_StillListsTheChannelWideRows_WhenTheCategoryHasNone()
    {
        // 这一档一行都没有、只有整渠道退订的人：他们照样收不到广播，页面照样要列出他们。
        await SeedAsync("other@example.com", NotificationType.Email, "marketing", Base.AddDays(-3));
        await SeedAsync("wide1@example.com", NotificationType.Email, null, Base.AddDays(-2));
        await SeedAsync("wide2@example.com", NotificationType.Email, null, Base.AddDays(-1));

        var page = await Service.GetPagedListAsync(new OptOutQueryDto
        {
            Channel = NotificationType.Email,
            Category = "broadcast",
            IncludeChannelWide = true,
        });

        page.Data!.TotalCount.ShouldBe(2);
        page.Data.Items.Select(o => o.Address).ShouldBe(["wide2@example.com", "wide1@example.com"]);
    }

    [Fact]
    public async Task GetPagedList_IncludeChannelWide_PagesAndCountsTheUnion()
    {
        // 分页与总数都按并集算（两条分类 + 一条整渠道 = 3），别的分类不计入。
        await SeedAsync("c1@example.com", NotificationType.Email, "broadcast", Base.AddDays(-3));
        await SeedAsync("wide@example.com", NotificationType.Email, null, Base.AddDays(-2));
        await SeedAsync("c2@example.com", NotificationType.Email, "broadcast", Base.AddDays(-1));
        await SeedAsync("other@example.com", NotificationType.Email, "marketing", Base.AddHours(-1));

        OptOutQueryDto Query(int pageIndex) => new()
        {
            Channel = NotificationType.Email,
            Category = "broadcast",
            IncludeChannelWide = true,
            PageIndex = pageIndex,
            PageSize = 2,
        };

        var first = await Service.GetPagedListAsync(Query(1));
        first.Data!.TotalCount.ShouldBe(3);
        first.Data.Items.Select(o => o.Address).ShouldBe(["c2@example.com", "wide@example.com"]);

        var second = await Service.GetPagedListAsync(Query(2));
        second.Data!.TotalCount.ShouldBe(3);
        second.Data.Items.Select(o => o.Address).ShouldBe(["c1@example.com"]);
    }

    [Fact]
    public async Task Register_RecordsTheOperatorAsSource_AndNormalisesTheAddress()
    {
        var result = await Service.RegisterAsync(new CreateOptOutDto
        {
            Address = "  Complainer@Example.COM ",
            Channel = NotificationType.Email,
            Category = "  ",
            Reason = " provider complaint ",
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Address.ShouldBe("complainer@example.com");
        result.Data.Category.ShouldBeNull("空白分类 = 整渠道退订，与一键链接同一口径");
        result.Data.Reason.ShouldBe("provider complaint");
        // 事后要能回答「这条是谁加的」：一键链接写 one-click link，管理端写操作者 id。
        result.Data.Source.ShouldBe($"admin:{TestHelper.DefaultTestUserId}");

        // 落库的那一行就是发送路径会查到的那一行：登记完即生效。
        (await Service.IsOptedOutAsync("complainer@example.com", NotificationType.Email, "anything")).ShouldBeTrue();
    }

    [Fact]
    public async Task Register_IsIdempotent_AndKeepsTheOriginalProvenance()
    {
        await SeedAsync("gone@example.com", NotificationType.Email, null, Base.AddDays(-3), source: "one-click link");

        var again = await Service.RegisterAsync(new CreateOptOutDto { Address = "GONE@example.com", Channel = NotificationType.Email, Reason = "call" });

        again.Succeeded.ShouldBeTrue();
        // 管理员补录不该覆盖收件人自己的记录：Source / Reason 留着最初那次退订的追溯信息。
        again.Data!.Source.ShouldBe("one-click link");
        again.Data.Reason.ShouldBeNull();
        (await DbContext.OptOuts.AsNoTracking().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Register_RequiresAnAddress()
    {
        var result = await Service.RegisterAsync(new CreateOptOutDto { Address = "   ", Channel = NotificationType.Sms });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Remove_DeletesTheRow_SoTheAddressReceivesAgain()
    {
        await SeedAsync("undo@example.com", NotificationType.Email, null, Base.AddDays(-1));
        var id = (await Service.GetPagedListAsync(new OptOutQueryDto())).Data!.Items.Single().Id;

        var result = await Service.RemoveAsync(id);

        result.Succeeded.ShouldBeTrue(result.Message);
        (await Service.IsOptedOutAsync("undo@example.com", NotificationType.Email)).ShouldBeFalse();
    }

    [Fact]
    public async Task Remove_AnUnknownId_Is404()
    {
        var result = await Service.RemoveAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }
}
