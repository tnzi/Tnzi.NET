namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 偏好上的时区：写入时怎么校验、存成什么样，读出时两个批量口各给什么。真库、真
/// <see cref="NotificationPreferenceService"/>。
/// </summary>
/// <remarks>
/// 判定本身（夏令时、缺口、重叠）由 <c>Services/QuietHoursTimeZoneTests</c> 覆盖；
/// 发送路径上真的按时区延后由 <c>QuietHoursSendPathTests</c> 覆盖。这里只管数据进出。
/// </remarks>
public class PreferenceTimeZoneTests : IntegrationTestBase
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<Preference, Guid>>(sp =>
            new EFCoreRepository<NotificationTestDbContext, Preference, Guid>(
                sp.GetRequiredService<NotificationTestDbContext>(), serviceProvider: sp));

        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(NotificationTestDbContext) });
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IUnitOfWork, EFCoreUnitOfWork<NotificationTestDbContext>>();

        services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();
    }

    private INotificationPreferenceService Preferences
        => ServiceProvider.GetRequiredService<INotificationPreferenceService>();

    // ── 写入 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ACanonicalTimeZone_IsStoredAsGiven()
    {
        var result = await SetAsync("America/Toronto");

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.QuietHoursTimeZone.ShouldBe("America/Toronto");
        (await ReloadAsync()).QuietHoursTimeZone.ShouldBe("America/Toronto");
    }

    /// <summary>
    /// ★★ 非规范大小写一律 400，与本进程之前查过什么无关。
    /// </summary>
    /// <remarks>
    /// <see cref="TimeZoneInfo.TryFindSystemTimeZoneById"/> 的进程内缓存不区分大小写：本用例先按规范写法查一次
    /// 把它放进缓存，再交小写写法。没有逐字比对时，小写写法会命中缓存而通过；而在一个从未查过该时区的进程里
    /// 同一个输入会被拒。先查后交正是复现「结果取决于查询顺序」的那个顺序。
    /// </remarks>
    [Fact]
    public async Task ANonCanonicalSpelling_IsRejected_EvenAfterTheCanonicalZoneWasLookedUp()
    {
        TimeZoneInfo.FindSystemTimeZoneById("America/Toronto").Id.ShouldBe("America/Toronto");

        var result = await SetAsync("america/toronto");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        (await DbContext.Set<Preference>().AsNoTracking().CountAsync()).ShouldBe(0);
    }

    /// <summary>
    /// ★ Windows 时区名在 Windows 主机上查得到，但它不是 IANA 标识：前端显示不了，
    /// 搬到 Linux 主机上的行为也取决于那台机器的 ICU。一律 400。
    /// </summary>
    [Fact]
    public async Task AWindowsTimeZoneName_IsRejected_AndNothingIsWritten()
    {
        var result = await SetAsync("Eastern Standard Time");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("IANA");
        result.ErrorCode.ShouldBe(ErrorCodes.NOTIFICATION_INVALID_TIME_ZONE);
        (await DbContext.Set<Preference>().AsNoTracking().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AnUnknownTimeZone_IsRejected()
    {
        var result = await SetAsync("Mars/Olympus");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        // ★ 前端据此把错误挂在时区选择上，而不是当成一次笼统的保存失败。
        result.ErrorCode.ShouldBe(ErrorCodes.NOTIFICATION_INVALID_TIME_ZONE);
    }

    /// <summary>
    /// ★ 开始等于结束是空区间，永远不会生效。存下来只会让人以为自己设了免打扰，所以 400，什么都不写。
    /// </summary>
    [Fact]
    public async Task AWindowThatStartsAndEndsAtTheSameTime_IsRejected_AndNothingIsWritten()
    {
        var result = await Preferences.SetPreferenceAsync(Alice, new SetNotificationPreferenceDto
        {
            Channel = "Email",
            IsEnabled = true,
            QuietHoursStart = new TimeOnly(22, 0),
            QuietHoursEnd = new TimeOnly(22, 0),
            QuietHoursTimeZone = "America/Toronto",
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        (await DbContext.Set<Preference>().AsNoTracking().CountAsync()).ShouldBe(0);
    }

    /// <summary>空白 = 没设 = 按 UTC 解释，存成 null 而不是空串。</summary>
    [Fact]
    public async Task ABlankTimeZone_IsStoredAsNull()
    {
        var result = await SetAsync("   ");

        result.Succeeded.ShouldBeTrue(result.Message);
        (await ReloadAsync()).QuietHoursTimeZone.ShouldBeNull();
    }

    /// <summary>更新路径同样写时区，清空就回到 UTC。</summary>
    [Fact]
    public async Task UpdatingAPreference_ReplacesOrClearsItsTimeZone()
    {
        (await SetAsync("America/Toronto")).Succeeded.ShouldBeTrue();

        (await SetAsync("Asia/Tokyo")).Succeeded.ShouldBeTrue();
        (await ReloadAsync()).QuietHoursTimeZone.ShouldBe("Asia/Tokyo");

        (await SetAsync(null)).Succeeded.ShouldBeTrue();
        (await ReloadAsync()).QuietHoursTimeZone.ShouldBeNull();
    }

    // ── 读出 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheUsersOwnList_CarriesTheTimeZone()
    {
        await SetAsync("Asia/Tokyo");

        var list = await Preferences.GetUserPreferencesAsync(Alice);

        list.Data!.ShouldHaveSingleItem().QuietHoursTimeZone.ShouldBe("Asia/Tokyo");
    }

    /// <summary>发送路径用的那个批量口把时区带出来。</summary>
    [Fact]
    public async Task TheZoneAwareBatchReader_CarriesTheZone()
    {
        await SetAsync("Asia/Tokyo");

        var windows = await Preferences.GetQuietHoursWindowsAsync([Alice], NotificationType.Email);

        windows.ShouldContainKey(Alice);
        var window = windows[Alice];
        window.Zone!.Id.ShouldBe("Asia/Tokyo");
        window.Start.ShouldBe(new TimeOnly(22, 0));
        window.End.ShouldBe(new TimeOnly(7, 0));
    }

    /// <summary>
    /// ★ 只认 UTC 时刻的老批量口：带时区的行换算成 UTC（东京 +9，无夏令时，所以与日期无关），
    /// 只认 UTC 的调用方据此判定仍然正确。
    /// </summary>
    [Fact]
    public async Task TheLegacyBatchReader_ReturnsAZonedWindowInUtc()
    {
        await SetAsync("Asia/Tokyo");

        var windows = await Preferences.GetQuietHoursAsync([Alice], NotificationType.Email);

        windows[Alice].ShouldBe((new TimeOnly(13, 0), new TimeOnly(22, 0)));
    }

    /// <summary>对照：没有时区的行原样返回，与引入时区之前逐字一致。</summary>
    [Fact]
    public async Task TheLegacyBatchReader_ReturnsAZonelessWindowAsStored()
    {
        await SetAsync(null);

        var windows = await Preferences.GetQuietHoursAsync([Alice], NotificationType.Email);

        windows[Alice].ShouldBe((new TimeOnly(22, 0), new TimeOnly(7, 0)));
    }

    /// <summary>
    /// ★ 写入时校验过、这台主机却查不到的时区（主机缺时区数据）：按 UTC 解释而不是跳过这个人：
    /// 跳过等于在他设的免打扰里照发。
    /// </summary>
    [Fact]
    public async Task AZoneThisHostCannotResolve_IsEvaluatedAsUtc()
    {
        await DbContext.Set<Preference>().AddAsync(new Preference
        {
            UserId = Alice,
            Channel = "Email",
            IsEnabled = true,
            QuietHoursStart = new TimeOnly(22, 0),
            QuietHoursEnd = new TimeOnly(7, 0),
            QuietHoursTimeZone = "Mars/Olympus",
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var windows = await Preferences.GetQuietHoursWindowsAsync([Alice], NotificationType.Email);

        windows.ShouldContainKey(Alice);
        windows[Alice].Contains(new DateTime(2026, 7, 15, 23, 0, 0, DateTimeKind.Utc)).ShouldBeTrue();
        windows[Alice].Contains(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc)).ShouldBeFalse();
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private async Task<Result<NotificationPreferenceDto>> SetAsync(string? timeZone)
    {
        var result = await Preferences.SetPreferenceAsync(Alice, new SetNotificationPreferenceDto
        {
            Channel = "Email",
            IsEnabled = true,
            QuietHoursStart = new TimeOnly(22, 0),
            QuietHoursEnd = new TimeOnly(7, 0),
            QuietHoursTimeZone = timeZone,
        });
        DbContext.ChangeTracker.Clear();
        return result;
    }

    private Task<Preference> ReloadAsync()
        => DbContext.Set<Preference>().AsNoTracking().SingleAsync(p => p.UserId == Alice);
}
