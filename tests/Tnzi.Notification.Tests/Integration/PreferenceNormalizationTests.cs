
namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 偏好的写入与单人判定，和群发过滤用同一个口径：渠道不分大小写、空白分类就是「整渠道」。真库（SQLite，字符串比较区分大小写）。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷一</b>：<c>SetPreferenceAsync</c> 把分类 <c>""</c> 原样存下。读取路径按 <c>Category == null</c> 找渠道级行、
/// 又把消息分类里的空白归成 null，于是这一行既不是渠道级、也匹配不上任何消息分类：管理端显示「已关闭」，发送时被忽略。
/// 清空过的表单输入框发来的就是 <c>""</c>。
/// </para>
/// <para>
/// <b>被保护的缺陷二</b>：<c>IsChannelEnabledAsync</c> 按原样比较渠道，而群发过滤按小写比较。管理端存的是 <c>"Email"</c>，
/// 消费方问 <c>IsChannelEnabledAsync(user, "email")</c> 得到「启用」，真正的发送路径却会把这个人排除。
/// </para>
/// </remarks>
public class PreferenceNormalizationTests : IntegrationTestBase
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<Preference, Guid>>(sp =>
            new EFCoreRepository<NotificationTestDbContext, Preference, Guid>(
                sp.GetRequiredService<NotificationTestDbContext>(), serviceProvider: sp));
        services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();
    }

    private INotificationPreferenceService Service => ServiceProvider.GetRequiredService<INotificationPreferenceService>();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankCategory_IsStoredAsTheChannelWideRow(string blank)
    {
        var result = await Service.SetPreferenceAsync(Alice, new SetNotificationPreferenceDto
        {
            Channel = "Email",
            Category = blank,
            IsEnabled = false
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        DbContext.ChangeTracker.Clear();
        var row = await DbContext.Set<Preference>().AsNoTracking().SingleAsync();
        row.Category.ShouldBeNull();

        var enabled = await Service.FilterEnabledUsersAsync([Alice], NotificationType.Email);
        enabled.ShouldNotContain(Alice, "关掉的渠道必须在发送路径上生效");
    }

    [Fact]
    public async Task BlankCategory_UpsertsOntoTheExistingChannelWideRow()
    {
        await Service.SetPreferenceAsync(Alice, new SetNotificationPreferenceDto { Channel = "Email", Category = null, IsEnabled = true });
        await Service.SetPreferenceAsync(Alice, new SetNotificationPreferenceDto { Channel = "Email", Category = "", IsEnabled = false });

        DbContext.ChangeTracker.Clear();
        var rows = await DbContext.Set<Preference>().AsNoTracking().ToListAsync();
        rows.ShouldHaveSingleItem().IsEnabled.ShouldBeFalse();
    }

    [Theory]
    [InlineData("email", null)]
    [InlineData("EMAIL", null)]
    [InlineData("email", "marketing")]
    [InlineData("email", "")]
    public async Task IsChannelEnabled_AgreesWithTheSendPathFilter(string askedChannel, string? askedCategory)
    {
        // 管理端存的写法是枚举名 "Email"。
        await Service.SetPreferenceAsync(Alice, new SetNotificationPreferenceDto { Channel = "Email", IsEnabled = false });

        var single = await Service.IsChannelEnabledAsync(Alice, askedChannel, askedCategory);
        var batch = (await Service.FilterEnabledUsersAsync([Alice], NotificationType.Email, askedCategory)).Contains(Alice);

        single.ShouldBe(batch);
        single.ShouldBeFalse();
    }

    [Fact]
    public async Task IsChannelEnabled_CategoryRowStillOverridesTheChannelRow()
    {
        await Service.SetPreferenceAsync(Alice, new SetNotificationPreferenceDto { Channel = "Email", IsEnabled = false });
        await Service.SetPreferenceAsync(Alice, new SetNotificationPreferenceDto { Channel = "Email", Category = "billing", IsEnabled = true });

        (await Service.IsChannelEnabledAsync(Alice, "email", "billing")).ShouldBeTrue();
        (await Service.IsChannelEnabledAsync(Alice, "email", "marketing")).ShouldBeFalse();
        (await Service.IsChannelEnabledAsync(Alice, "sms")).ShouldBeTrue("没有偏好行的渠道默认启用");
    }
}
