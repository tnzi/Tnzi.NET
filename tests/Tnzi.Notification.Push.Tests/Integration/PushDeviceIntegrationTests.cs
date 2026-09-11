using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Domain.Repositories;
using Tnzi.EFCore;
using Tnzi.Mapping;
using Tnzi.Mapster;
using Tnzi.Notification.Push.Dtos;
using Tnzi.Notification.Push.Entities;
using Tnzi.Notification.Push.Entities.Configs;
using Tnzi.Notification.Push.Mappings;
using Tnzi.Notification.Push.Metadata;
using Tnzi.Security;
using Tnzi.TestBase;

namespace Tnzi.Notification.Push.Tests.Integration;

/// <summary>
/// 设备注册表在<b>真实仓储</b>上的行为（SQLite 内存库）。
/// </summary>
/// <remarks>
/// 不用替身仓储：<c>PushDeviceService</c> 的查询走
/// <c>AsQueryable().AsNoTracking().ToListAsync()</c>，那是 EF 的异步查询提供程序，
/// 普通 <c>IQueryable</c> 顶不住 —— 换句话说，一个能跑起来的替身必然要把这些调用改掉，
/// 那样测的就不是这段代码了。
/// </remarks>
public class PushDeviceIntegrationTests : IntegratedTestBase<PushTestDbContext>
{
    private static readonly Guid Me = TestHelper.DefaultTestUserId;
    private static readonly Guid Someone = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NoDevices = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private IPushDeviceService Service => ServiceProvider.GetRequiredService<IPushDeviceService>();

    public PushDeviceIntegrationTests()
    {
        // 真实服务走 MapTo，而 MapperExtensions 持有的是**静态**映射器，正常由 MapsterModule
        // 在应用启动时装上 —— 测试里没有那一步。同 Notification / Chat / Audit 的集成基类。
        var config = new TypeAdapterConfig();

        // ★ 跑**真实的** PushDeviceMappingConfig，而不是在测试里重写一遍同样的映射：
        // 后者会让「服务里手写 new PushDeviceDto 绕开了掩码」这类回归照样绿。
        // 上下文实现是 Tnzi.Mapster 的 internal 类型，故反射构造；类型名若改，
        // 这里会当场抛异常，不会静默跳过映射。
        var contextType = typeof(MapsterModule).Assembly
            .GetType("Tnzi.Mapster.Adapters.MapsterMappingConfigContext")!;
        var mappingContext = (IMappingConfigContext)Activator.CreateInstance(contextType, config)!;
        new PushDeviceMappingConfig().Configure(mappingContext);

        MapperExtensions.SetMapper(new Mapper(config));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<PushDevice, Guid>>(sp =>
            new EFCoreRepository<PushTestDbContext, PushDevice, Guid>(
                sp.GetRequiredService<PushTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IPushDeviceService, PushDeviceService>();
    }

    private async Task<PushDevice> SeedAsync(Guid userId, string token)
    {
        var device = new PushDevice
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = token,
            Platform = DevicePlatform.Android,
            LastSeenAt = DateTime.UtcNow
        };
        DbContext.Set<PushDevice>().Add(device);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return device;
    }

    private Task<List<PushDevice>> RowsAsync()
        => DbContext.Set<PushDevice>().AsNoTracking().ToListAsync();

    /// <summary>
    /// ★★ 解析必须把「没有任何设备」的用户单独报出来，不能只少返回几条。
    /// </summary>
    /// <remarks>
    /// 给 3 个人、其中 1 个没注册过设备时，扁平列表只会有 2 条，而调用方拿不到任何信号
    /// 说明第 3 个人<b>没有被通知到</b> —— 他不会出现在任何一条投递记录里，
    /// 报表上与「这次本来就没打算发给他」完全一致。这是本模块最容易静默失效的一处。
    /// </remarks>
    [Fact]
    public async Task Resolution_names_the_users_that_have_no_device_instead_of_silently_dropping_them()
    {
        await SeedAsync(Me, "token-me-phone");
        await SeedAsync(Me, "token-me-tablet");
        await SeedAsync(Someone, "token-someone");

        var resolution = await Service.ResolveRecipientsAsync(new[] { Me, Someone, NoDevices });

        resolution.Recipients.Count.ShouldBe(3);
        resolution.UsersWithoutDevices.ShouldBe(new[] { NoDevices });
        resolution.IsComplete.ShouldBeFalse();
    }

    /// <summary>
    /// ★ 解析出的收件人必须带 <c>UserId</c>。
    /// </summary>
    /// <remarks>
    /// 投递管线用它做偏好与频次上限过滤（那两道过滤对 <c>UserId</c> 为空的收件人
    /// <b>一律放行</b>），也用它决定这条消息归谁的站内收件箱。只填 Address 的话，
    /// 一个明确把推送渠道关掉的用户照样会收到推送。
    /// </remarks>
    [Fact]
    public async Task Resolved_recipients_carry_the_user_id_so_preference_filters_still_apply()
    {
        await SeedAsync(Me, "token-me-phone");

        var resolution = await Service.ResolveRecipientsAsync(new[] { Me });

        var recipient = resolution.Recipients.ShouldHaveSingleItem();
        recipient.UserId.ShouldBe(Me);
        recipient.Address.ShouldBe("token-me-phone");
    }

    /// <summary>全员都有设备时，解析结果是完整的。</summary>
    [Fact]
    public async Task Resolution_is_complete_when_every_user_has_at_least_one_device()
    {
        await SeedAsync(Me, "token-me-phone");
        await SeedAsync(Someone, "token-someone");

        var resolution = await Service.ResolveRecipientsAsync(new[] { Me, Someone });

        resolution.UsersWithoutDevices.ShouldBeEmpty();
        resolution.IsComplete.ShouldBeTrue();
    }

    /// <summary>
    /// ★★★ 一个令牌已经挂在<b>别人</b>名下时，注册要被拒绝，那一行原封不动。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>本条曾经断言的是相反的行为</b>（「改挂给调用者」），而那正是缺陷本身：令牌是凭据，
    /// 任何已登录用户只要拿到别人的令牌，就能把那一行改挂给自己 —— 受害者从此收不到推送，
    /// 而两边的接口都返回成功、行数不变，零症状。
    /// </para>
    /// <para>
    /// ★ 拒绝会挡掉一个真实场景（前一个用户没退出登录，同一台设备换人登录），这是刻意的：
    /// 接管错了是<b>无声地</b>掐掉某个人的推送，拒绝错了是新用户收到一条说得出补救办法的
    /// 错误。正常的换人先走登出（那会删掉或摘掉这一行），重装 App 则会拿到新令牌。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Registering_a_token_that_belongs_to_someone_else_is_refused()
    {
        await SeedAsync(Someone, "shared-device-token");

        var result = await Service.RegisterAsync(new RegisterPushDeviceDto
        {
            Token = "shared-device-token",
            Platform = DevicePlatform.Ios
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);

        // 旧主人的投递地址一个字节都没动。
        (await RowsAsync()).ShouldHaveSingleItem().UserId.ShouldBe(Someone);
        var previousOwner = await Service.ResolveRecipientsAsync(new[] { Someone });
        previousOwner.Recipients.ShouldHaveSingleItem();
    }

    /// <summary>
    /// 对照：<b>无主</b>的那一行仍然可以被认领 —— 那是登出后摘掉归属、或纯匿名注册留下的行，
    /// 认领它正是本意。少了这条，上面那条会把「一律拒绝」也读成正确。
    /// </summary>
    [Fact]
    public async Task Registering_an_unowned_token_claims_the_existing_row()
    {
        var device = await SeedAsync(Someone, "orphan-token");
        await DbContext.Set<PushDevice>().Where(d => d.Id == device.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.UserId, (Guid?)null));
        DbContext.ChangeTracker.Clear();

        var result = await Service.RegisterAsync(new RegisterPushDeviceDto
        {
            Token = "orphan-token",
            Platform = DevicePlatform.Ios
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        (await RowsAsync()).ShouldHaveSingleItem().UserId.ShouldBe(Me);
    }

    /// <summary>重复注册同一令牌是刷新，不是新增。</summary>
    [Fact]
    public async Task Registering_the_same_token_twice_refreshes_the_single_row()
    {
        await Service.RegisterAsync(new RegisterPushDeviceDto { Token = "t1", Platform = DevicePlatform.Android });
        await Service.RegisterAsync(new RegisterPushDeviceDto
        {
            Token = "t1",
            Platform = DevicePlatform.Web,
            DeviceName = "Chrome"
        });

        var row = (await RowsAsync()).ShouldHaveSingleItem();
        row.Platform.ShouldBe(DevicePlatform.Web);
        row.DeviceName.ShouldBe("Chrome");
    }

    /// <summary>★ 退役一个死令牌必须<b>真的删掉</b>那一行，之后解析不到它。</summary>
    [Fact]
    public async Task Retiring_a_dead_token_removes_the_row_for_good()
    {
        await SeedAsync(Me, "dead-token");

        var retired = await Service.RetireAsync("dead-token");

        retired.ShouldBe(1);
        (await RowsAsync()).ShouldBeEmpty();

        var resolution = await Service.ResolveRecipientsAsync(new[] { Me });
        resolution.UsersWithoutDevices.ShouldBe(new[] { Me });
    }

    /// <summary>退役一个不存在的令牌是无操作，不抛异常 —— 它跑在投递路径的收尾上。</summary>
    [Theory]
    [InlineData("never-registered")]
    [InlineData("")]
    public async Task Retiring_an_unknown_token_is_a_no_op(string token)
    {
        (await Service.RetireAsync(token)).ShouldBe(0);
    }

    /// <summary>
    /// ★★ 登出路径：客户端手里<b>只有令牌</b>，必须能凭它注销。
    /// </summary>
    /// <remarks>
    /// 这条守的是一处会整条断掉的链路。设备列表返回的是掩码令牌，所以客户端无法
    /// 由令牌反查出行 id；若只提供按 id 注销，登出时它<b>无从定位自己那一行</b>，
    /// 于是一台已经登出的设备继续收推送 —— 而后端一切正常。
    /// </remarks>
    [Fact]
    public async Task Signing_out_unregisters_by_token_because_the_client_has_no_row_id()
    {
        await SeedAsync(Me, "my-device-token");

        var result = await Service.UnregisterByTokenAsync("my-device-token");

        result.Succeeded.ShouldBeTrue();
        (await RowsAsync()).ShouldBeEmpty();
    }

    /// <summary>
    /// ★ 登出是幂等的：令牌不在表里也算成功。
    /// </summary>
    /// <remarks>
    /// 重复登出、或者这个令牌已被投递路径判定失效而退役掉，都是「已经不再收推送」——
    /// 那是正确状态，报错会让它长得像一次失败，而客户端除了重试无事可做。
    /// </remarks>
    [Theory]
    [InlineData("never-registered-token")]
    [InlineData("   ")]
    public async Task Signing_out_twice_is_not_an_error(string token)
    {
        var result = await Service.UnregisterByTokenAsync(token);

        result.Succeeded.ShouldBe(!string.IsNullOrWhiteSpace(token));
    }

    /// <summary>
    /// ★ 按令牌注销限定在当前用户：那台设备若已换人登录，不能删掉新主人那一行。
    /// </summary>
    [Fact]
    public async Task Unregistering_by_token_does_not_touch_a_row_that_has_since_moved_to_someone_else()
    {
        await SeedAsync(Someone, "handed-over-token");

        var result = await Service.UnregisterByTokenAsync("handed-over-token");

        result.Succeeded.ShouldBeTrue();      // 幂等：对我而言「已经不收了」
        (await RowsAsync()).ShouldHaveSingleItem().UserId.ShouldBe(Someone);   // 但没动别人的
    }

    /// <summary>注销只能动自己的设备；别人的答 404 而不是 403，免得这个端点变成存在性探针。</summary>
    [Fact]
    public async Task Unregistering_someone_elses_device_is_not_found_rather_than_forbidden()
    {
        var theirs = await SeedAsync(Someone, "not-mine");

        var result = await Service.UnregisterAsync(theirs.Id);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        (await RowsAsync()).ShouldHaveSingleItem();
    }

    /// <summary>★ 设备列表对外只给掩码，不给完整令牌。</summary>
    [Fact]
    public async Task The_device_list_never_returns_the_raw_token()
    {
        const string token = "a-very-long-and-recognisable-fcm-token-value";
        await SeedAsync(Me, token);

        var devices = (await Service.GetMyDevicesAsync()).Data.ShouldNotBeNull();

        var dto = devices.ShouldHaveSingleItem();
        dto.TokenMask.ShouldNotBe(token);
        dto.TokenMask.ShouldEndWith(token[^8..]);
    }

    /// <summary>
    /// 模拟一次请求边界：清掉变更跟踪器。
    /// </summary>
    /// <remarks>
    /// 真实部署里每个 HTTP 请求一个 DbContext，而这些用例整条跑在同一个上下文里。
    /// 「先注册、再退役」这类跨请求的序列不清一下就会撞上 EF 的实体身份冲突 ——
    /// 那是夹具的产物，不是被测行为。<c>SeedAsync</c> 出于同一原因也在末尾清一次。
    /// </remarks>
    private void NextRequest() => DbContext.ChangeTracker.Clear();

    private static RegisterPushDeviceDto Input(
        string token, string? deviceName = null, string? externalDeviceId = null) => new()
    {
        Token = token,
        Platform = DevicePlatform.Android,
        DeviceName = deviceName,
        ExternalDeviceId = externalDeviceId
    };

    /// <summary>
    /// 签发出的密钥明文<b>不落库</b>，库里只有它的哈希；行是无主的，Id 就是签发返回的那个。
    /// </summary>
    [Fact]
    public async Task Anonymous_registration_stores_only_the_hash_of_the_key_it_hands_out()
    {
        var issued = (await Service.RegisterAnonymousAsync(null, Input("tok-anon"))).Data.ShouldNotBeNull();

        var row = (await RowsAsync()).ShouldHaveSingleItem();
        row.Id.ShouldBe(issued.DeviceId);
        row.UserId.ShouldBeNull();
        row.DeviceKeyHash.ShouldNotBeNullOrWhiteSpace();
        row.DeviceKeyHash.ShouldNotBe(issued.DeviceKey);
    }

    /// <summary>
    /// ★★ 行被退役删掉后，同一枚密钥重建出来的设备 Id <b>逐字相同</b>。
    /// </summary>
    /// <remarks>
    /// 这是整条匿名路径的承重点。这个 Id 是消费方业务记录里的归属键（哪台设备提交了这份表单），
    /// 而令牌被网关判死时整行会被 <c>RetireAsync</c> 删掉 —— Id 若是随机生成的，客户端带着
    /// 同一枚密钥回来只会拿到一个<b>新</b> Id，此前所有表单的回执就永久断了，
    /// 而且毫无症状：表单还在、状态正常，只是再没有推送来过。
    /// </remarks>
    [Fact]
    public async Task A_retired_anonymous_row_comes_back_with_the_same_device_id()
    {
        var issued = (await Service.RegisterAnonymousAsync(null, Input("tok-dead"))).Data.ShouldNotBeNull();
        NextRequest();

        await Service.RetireAsync("tok-dead");
        (await RowsAsync()).ShouldBeEmpty();
        NextRequest();

        var rebuilt = (await Service.RegisterAnonymousAsync(issued.DeviceKey, Input("tok-fresh")))
            .Data.ShouldNotBeNull();

        rebuilt.DeviceId.ShouldBe(issued.DeviceId);
        (await RowsAsync()).ShouldHaveSingleItem().Token.ShouldBe("tok-fresh");
    }

    /// <summary>令牌轮换只是换掉同一行的令牌，不产生第二行、也不换 Id。</summary>
    [Fact]
    public async Task Refreshing_rotates_the_token_on_the_same_row()
    {
        var issued = (await Service.RegisterAnonymousAsync(null, Input("tok-v1"))).Data.ShouldNotBeNull();
        NextRequest();

        await Service.RegisterAnonymousAsync(issued.DeviceKey, Input("tok-v2"));

        var row = (await RowsAsync()).ShouldHaveSingleItem();
        row.Id.ShouldBe(issued.DeviceId);
        row.Token.ShouldBe("tok-v2");
        row.DeviceKeyHash.ShouldNotBeNull();
    }

    /// <summary>
    /// ★★ 按设备解析必须把「已经找不到」的那些 Id 单独报出来。
    /// </summary>
    /// <remarks>
    /// 一台设备退役后（卸载、令牌被判死）它的 Id 还留在业务记录里，而那份记录的回执
    /// <b>发不出去了</b>。只少返回一条的话，它与「这次本来就没打算发给它」完全一致。
    /// 同 <c>ResolveRecipientsAsync</c> 那条不变量。
    /// </remarks>
    [Fact]
    public async Task Resolving_by_device_id_names_the_devices_that_are_gone()
    {
        var issued = (await Service.RegisterAnonymousAsync(null, Input("tok-live"))).Data.ShouldNotBeNull();
        var gone = Guid.NewGuid();

        var resolution = await Service.ResolveByDeviceIdsAsync([issued.DeviceId, gone]);

        resolution.Recipients.ShouldHaveSingleItem().Address.ShouldBe("tok-live");
        resolution.DeviceIdsNotFound.ShouldHaveSingleItem().ShouldBe(gone);
        resolution.IsComplete.ShouldBeFalse();
    }

    /// <summary>
    /// 匿名收件人不带 <c>UserId</c> —— 偏好与频次上限按人算，而这台设备没有人。
    /// </summary>
    /// <remarks>
    /// 两个过滤器都对无主收件人<b>原样放行</b>，退订仍按地址生效，所以匿名设备照样退得掉。
    /// 填一个假的 UserId 会让它落进别人的偏好判定里。
    /// </remarks>
    [Fact]
    public async Task Anonymous_recipients_carry_no_user_id_so_the_per_user_filters_let_them_through()
    {
        var issued = (await Service.RegisterAnonymousAsync(null, Input("tok-nouser"))).Data.ShouldNotBeNull();

        var resolution = await Service.ResolveByDeviceIdsAsync([issued.DeviceId]);

        resolution.Recipients.ShouldHaveSingleItem().UserId.ShouldBeNull();
    }

    /// <summary>登录不清掉这台设备已有的匿名身份 —— 两个寻址维度并存。</summary>
    /// <remarks>
    /// 这台设备可能先以匿名身份提交过表单、用户后来才注册账号。清掉匿名身份，
    /// 那些表单的回执就静默停发了，而触发它的只是一次正常登录。
    /// </remarks>
    [Fact]
    public async Task Signing_in_on_a_device_does_not_clear_its_anonymous_identity()
    {
        var issued = (await Service.RegisterAnonymousAsync(null, Input("tok-both"))).Data.ShouldNotBeNull();
        NextRequest();

        await Service.RegisterAsync(Input("tok-both"));

        var row = (await RowsAsync()).ShouldHaveSingleItem();
        row.Id.ShouldBe(issued.DeviceId);
        row.UserId.ShouldBe(Me);
        row.DeviceKeyHash.ShouldNotBeNull();
    }

    /// <summary>登出的是人不是设备：还挂着匿名身份的行只解除用户归属，不删。</summary>
    [Fact]
    public async Task Signing_out_keeps_the_row_when_it_still_carries_an_anonymous_identity()
    {
        var issued = (await Service.RegisterAnonymousAsync(null, Input("tok-both"))).Data.ShouldNotBeNull();
        NextRequest();
        await Service.RegisterAsync(Input("tok-both"));
        NextRequest();

        await Service.UnregisterByTokenAsync("tok-both");

        var row = (await RowsAsync()).ShouldHaveSingleItem();
        row.Id.ShouldBe(issued.DeviceId);
        row.UserId.ShouldBeNull();
        row.DeviceKeyHash.ShouldNotBeNull();
    }

    /// <summary>
    /// ★★★ 一个<b>未认证</b>的调用者不得动一行属于某个账号的设备。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>本条曾经断言的是相反的行为</b>（「把登录归属带过来」），而那是本模块最严重的
    /// 一处：这条路径是 <c>[AllowAnonymous]</c> 的，它会删掉占着令牌的那一行、再把它的
    /// <c>UserId</c> 搬到一个<b>攻击者持有密钥</b>的身份上；随后一次普通刷新就把令牌换成
    /// 攻击者自己的设备，那个用户的推送从此发到攻击者手上。
    /// </para>
    /// <para>
    /// ★ 「可能是同一台设备」在这里不能作为放行理由：登录归属只能由那个登录着的人自己动。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_anonymous_registration_cannot_take_over_a_token_that_belongs_to_an_account()
    {
        await SeedAsync(Me, "tok-carry");

        var result = await Service.RegisterAnonymousAsync(null, Input("tok-carry"));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);

        var row = (await RowsAsync()).ShouldHaveSingleItem();
        row.UserId.ShouldBe(Me);
        row.DeviceKeyHash.ShouldBeNull();
    }

    /// <summary>
    /// 对照：占着令牌的那一行<b>无主</b>时照旧重建（客户端清掉了本地密钥）。
    /// 少了这条，上面那条会把「匿名注册一律拒绝」也读成正确。
    /// </summary>
    [Fact]
    public async Task An_anonymous_registration_rebuilds_over_an_unowned_row()
    {
        var first = (await Service.RegisterAnonymousAsync(null, Input("tok-rebuild"))).Data.ShouldNotBeNull();
        NextRequest();

        // 客户端把本地密钥弄丢了，用同一个令牌重新签发一枚。
        var second = (await Service.RegisterAnonymousAsync(null, Input("tok-rebuild"))).Data.ShouldNotBeNull();

        second.DeviceId.ShouldNotBe(first.DeviceId);
        var row = (await RowsAsync()).ShouldHaveSingleItem();
        row.Id.ShouldBe(second.DeviceId);
        row.UserId.ShouldBeNull();
    }

    /// <summary>匿名注销时若这台设备还登录着人，只清匿名身份，不删行。</summary>
    [Fact]
    public async Task Unregistering_anonymously_keeps_the_row_when_a_user_is_still_signed_in()
    {
        var issued = (await Service.RegisterAnonymousAsync(null, Input("tok-both"))).Data.ShouldNotBeNull();
        NextRequest();
        await Service.RegisterAsync(Input("tok-both"));
        NextRequest();

        await Service.UnregisterAnonymousAsync(issued.DeviceKey);

        var row = (await RowsAsync()).ShouldHaveSingleItem();
        row.UserId.ShouldBe(Me);
        row.DeviceKeyHash.ShouldBeNull();
    }

    /// <summary>
    /// admin 的 <c>AnonymousOnly</c> 筛选按「这台设备背后有没有人」分组，两个方向都要能翻译成 SQL。
    /// </summary>
    /// <remarks>
    /// 判据刻意是 <c>UserId == null</c> 而不是 <c>DeviceKeyHash != null</c>：后者会漏掉
    /// 「登录用户 + 匿名身份并存」的那些行，而运维在这个筛选里想看的正是有没有人。
    /// 走真实仓储是因为这个条件带一个三元表达式，在内存里恒真、在 SQL 上未必翻译得出来。
    /// </remarks>
    [Fact]
    public async Task The_anonymous_filter_splits_rows_by_whether_a_user_is_behind_them()
    {
        await SeedAsync(Me, "tok-owned");
        await Service.RegisterAnonymousAsync(null, Input("tok-orphan"));
        NextRequest();

        var anonymous = (await Service.GetPagedListAsync(new PushDeviceQueryDto { AnonymousOnly = true }))
            .Data.ShouldNotBeNull();
        var owned = (await Service.GetPagedListAsync(new PushDeviceQueryDto { AnonymousOnly = false }))
            .Data.ShouldNotBeNull();
        var everything = (await Service.GetPagedListAsync(new PushDeviceQueryDto()))
            .Data.ShouldNotBeNull();

        anonymous.Items.ShouldHaveSingleItem().UserId.ShouldBeNull();
        owned.Items.ShouldHaveSingleItem().UserId.ShouldBe(Me);
        everything.Items.Count.ShouldBe(2);
    }

    /// <summary>
    /// ★★ 密钥<b>只在签发那一次</b>回给客户端，带着密钥来刷新时不回。
    /// </summary>
    /// <remarks>
    /// 注册与刷新是同一个方法，「响应里有没有密钥」就是客户端判断本次是不是签发的唯一依据 ——
    /// 它因此不需要自己知道「这是不是首次」。刷新也回一份密钥的话，那个值会白白多走一趟网络、
    /// 多进一次日志，而客户端手上本来就有。
    /// </remarks>
    [Fact]
    public async Task The_key_comes_back_only_when_it_is_issued()
    {
        var issued = (await Service.RegisterAnonymousAsync(null, Input("tok-a"))).Data.ShouldNotBeNull();
        issued.DeviceKey.ShouldNotBeNullOrWhiteSpace();
        NextRequest();

        var refreshed = (await Service.RegisterAnonymousAsync(issued.DeviceKey, Input("tok-b")))
            .Data.ShouldNotBeNull();

        refreshed.DeviceKey.ShouldBeNull();
        refreshed.DeviceId.ShouldBe(issued.DeviceId);
    }

    /// <summary>
    /// ★★ 空白密钥算「没带密钥」，不算「密钥是空串」。
    /// </summary>
    /// <remarks>
    /// 客户端把一个读不到值的存储项原样塞进请求头是很常见的。把空串当成密钥的话，它会哈希出一个
    /// 完全合法的值，于是<b>所有这样的客户端共用同一行</b> —— 彼此不停顶掉对方的令牌，
    /// 而每一次注册都返回成功。
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_device_key_is_treated_as_no_key_rather_than_as_a_key(string blank)
    {
        var first = (await Service.RegisterAnonymousAsync(blank, Input("tok-1"))).Data.ShouldNotBeNull();
        NextRequest();
        var second = (await Service.RegisterAnonymousAsync(blank, Input("tok-2"))).Data.ShouldNotBeNull();

        first.DeviceKey.ShouldNotBeNullOrWhiteSpace();
        second.DeviceKey.ShouldNotBeNullOrWhiteSpace();
        second.DeviceId.ShouldNotBe(first.DeviceId);
        (await RowsAsync()).Count.ShouldBe(2);
    }

    /// <summary>
    /// ★★ 客户端自报的设备标识<b>存下来供辨认，但一处寻址都不参与</b>，也不做唯一约束。
    /// </summary>
    /// <remarks>
    /// 它是客户端说什么就是什么的值，服务端验不了，而且会作为辨认信息流进业务表、日志与管理界面。
    /// 拿它当寻址键，攻击就是一次普通请求：报上别人的标识加自己的令牌。
    /// <para>
    /// 不唯一也是刻意的：iOS 的 <c>identifierForVendor</c> 在同一 vendor 的多个 App 之间共享同一个值，
    /// 于是同一个标识<b>合法地</b>出现在多行；而唯一约束还会让人靠抢占一个值来阻止真实设备注册。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_external_device_id_is_kept_for_recognition_but_addresses_nothing()
    {
        var a = (await Service.RegisterAnonymousAsync(null, Input("tok-a", externalDeviceId: "IDFV-SHARED")))
            .Data.ShouldNotBeNull();
        NextRequest();
        var b = (await Service.RegisterAnonymousAsync(null, Input("tok-b", externalDeviceId: "IDFV-SHARED")))
            .Data.ShouldNotBeNull();
        NextRequest();

        // 同一个标识落在两行上，谁都没被顶掉
        var rows = await RowsAsync();
        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(r => r.ExternalDeviceId == "IDFV-SHARED");
        b.DeviceId.ShouldNotBe(a.DeviceId);

        // 而寻址只认设备 Id
        var resolution = await Service.ResolveByDeviceIdsAsync([a.DeviceId]);
        resolution.Recipients.ShouldHaveSingleItem().Address.ShouldBe("tok-a");
    }

    /// <summary>
    /// 超长的设备标识<b>就地拒绝而不是截断</b>，整次注册不落库。
    /// </summary>
    /// <remarks>
    /// 超出上限通常意味着客户端把别的东西塞进来了（整段 JSON、一条日志）。截断会把那个错误变成
    /// 一行看起来正常、却永远对不上任何设备的数据，而客户端一无所知。
    /// </remarks>
    [Fact]
    public async Task An_oversized_external_device_id_is_refused_rather_than_truncated()
    {
        var oversized = new string('x', PushDeviceConfiguration.ExternalDeviceIdMaxLength + 1);

        var result = await Service.RegisterAnonymousAsync(null, Input("tok-x", externalDeviceId: oversized));

        result.Data.ShouldBeNull();
        (await RowsAsync()).ShouldBeEmpty();
    }

    /// <summary>admin 能按客户端自报的标识把清单收窄 —— 运维手上通常只有那个值。</summary>
    [Fact]
    public async Task The_admin_list_can_be_narrowed_by_the_external_device_id()
    {
        await Service.RegisterAnonymousAsync(null, Input("tok-a", externalDeviceId: "IDFV-A"));
        NextRequest();
        await Service.RegisterAnonymousAsync(null, Input("tok-b", externalDeviceId: "IDFV-B"));
        NextRequest();

        var page = (await Service.GetPagedListAsync(new PushDeviceQueryDto { ExternalDeviceId = "IDFV-A" }))
            .Data.ShouldNotBeNull();

        page.Items.ShouldHaveSingleItem().ExternalDeviceId.ShouldBe("IDFV-A");
    }

    /// <summary>
    /// ★★ 不是本服务签发形态的密钥<b>就地拒绝</b>，不为它建行。
    /// </summary>
    /// <remarks>
    /// 刷新路径必须接受查不到对应行的密钥（退役后要能重建），所以「这是不是我签发的」查不出来。
    /// 不校验形态的话，一个为了省掉「存密钥」那一行而直接填 <c>identifierForVendor</c> 的客户端
    /// 会被照单全收 —— 而「服务端签发所以熵有保证」这条就整个落空了，且没有任何症状：
    /// 注册成功、推送正常，只是这台设备的全部凭据变成了一个流进业务表与日志的公开值。
    /// </remarks>
    [Fact]
    public async Task A_key_that_is_not_in_the_issued_format_is_refused_rather_than_accepted()
    {
        var result = await Service.RegisterAnonymousAsync(
            "3F2504E0-4F89-11D3-9A0C-0305E82C3301", Input("tok-x"));

        result.Data.ShouldBeNull();
        (await RowsAsync()).ShouldBeEmpty();
    }

    /// <summary>
    /// ★ 密钥两侧的空白在注册与注销两条路上必须<b>同样</b>被去掉。
    /// </summary>
    /// <remarks>
    /// 注册那条一直在 <c>Trim()</c>，注销那条曾经没有 —— 于是一个把密钥连同换行一起存下来的
    /// 客户端，注册时落的是去空白后的哈希、注销时算的是带空白的哈希，两者对不上。
    /// 而注销是<b>幂等</b>的：对不上就当"已经不在了"返回成功，于是客户端收到 200、
    /// 那一行却原封不动地留着继续收推送。
    /// </remarks>
    [Fact]
    public async Task A_key_with_surrounding_whitespace_still_matches_the_row_it_registered()
    {
        var issued = (await Service.RegisterAnonymousAsync(null, Input("tok-x"))).Data.ShouldNotBeNull();
        NextRequest();

        await Service.UnregisterAnonymousAsync($"  {issued.DeviceKey}  ");

        (await RowsAsync()).ShouldBeEmpty();
    }

    /// <summary>
    /// 未知密钥与已退役的密钥<b>回答同一句话</b>，且都不是错误。
    /// </summary>
    /// <remarks>
    /// 区分开就是在帮人试探哪些密钥是真的（<c>OneTimeToken</c> 的类注释里那条）；
    /// 而报错还会让「已经不再收推送」这个正确状态看起来像一次失败。
    /// </remarks>
    [Fact]
    public async Task An_unknown_device_key_is_answered_the_same_way_as_a_retired_one()
    {
        var issued = (await Service.RegisterAnonymousAsync(null, Input("tok-x"))).Data.ShouldNotBeNull();
        NextRequest();

        await Service.UnregisterAnonymousAsync(issued.DeviceKey);
        NextRequest();

        var afterRetired = await Service.UnregisterAnonymousAsync(issued.DeviceKey);
        var neverSeen = await Service.UnregisterAnonymousAsync(OneTimeToken.Create());

        afterRetired.Message.ShouldBe(neverSeen.Message);
        afterRetired.Code.ShouldBe(neverSeen.Code);
        (await RowsAsync()).ShouldBeEmpty();
    }
}

/// <summary>本模块集成测试用的 DbContext：只装 <see cref="PushDevice"/>。</summary>
public class PushTestDbContext : TnziDbContext<PushTestDbContext>
{
    public PushTestDbContext(DbContextOptions<PushTestDbContext> options, Security.Claims.ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<PushDevice> PushDevices => Set<PushDevice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new Entities.Configs.PushDeviceConfiguration());
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
