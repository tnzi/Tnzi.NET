using Tnzi.MultiTenancy;

namespace Tnzi.System.Tests.Integration;

/// <summary>
/// 参数表的管理端读路径按<b>调用者的租户</b>收口：默认只回答 Global + 本租户的 Tenant 行，
/// User 行只在显式指定 <c>scope=User</c> 时返回。
/// </summary>
/// <remarks>
/// <para>
/// <c>Setting</c> 不实现 <c>IMultiTenant</c>（租户归属放在 <c>ScopeId</c> 列，Global 行没有租户），
/// 所以全局租户过滤器管不到它；此前 <c>GetSettingsAsync</c> 只按分组过滤，多租户部署里
/// 租户 A 的管理员打开参数页就拿到了租户 B 的全部 Tenant 行与所有用户的 User 行，掩码只盖住加密行。
/// 分组计数走同一条口径。
/// </para>
/// <para>
/// 租户归属由身份决定，不由客户端参数决定：租户内的调用者请求别的租户返回 403 而不是静默改写。
/// </para>
/// </remarks>
public class SettingScopeIntegrationTests : IntegratedTestBase<SettingScopeIntegrationTests.SettingTestDbContext>
{
    public class SettingTestDbContext : TnziDbContext<SettingTestDbContext>
    {
        public SettingTestDbContext(DbContextOptions<SettingTestDbContext> options, Tnzi.Security.Claims.ICurrentUser currentUser)
            : base(options, currentUser)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new Entities.Configs.SettingConfiguration());
            base.OnModelCreating(modelBuilder);
            TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
        }
    }

    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid UserX = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid UserY = Guid.Parse("22222222-0000-0000-0000-000000000002");

    public SettingScopeIntegrationTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
        Seed().GetAwaiter().GetResult();
    }

    private async Task Seed()
    {
        DbContext.Set<Setting>().AddRange(
            Row("Site.Title", "A", SettingScope.Global, null),
            Row("Site.Title", "A", SettingScope.Tenant, TenantA.ToString()),
            Row("Site.Title", "A", SettingScope.Tenant, TenantB.ToString()),
            Row("Ui.Theme", "B", SettingScope.User, UserX.ToString()),
            Row("Ui.Theme", "B", SettingScope.User, UserY.ToString()),
            new Setting { Key = "Mail.Password", Value = "cipher", Group = "A", Scope = SettingScope.Tenant, ScopeId = TenantA.ToString(), IsEncrypted = true });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private static Setting Row(string key, string group, SettingScope scope, string? scopeId) => new()
    {
        Key = key,
        Value = $"{scope}:{scopeId ?? "-"}",
        Group = group,
        Scope = scope,
        ScopeId = scopeId
    };

    /// <summary><paramref name="tenantId"/> 为 null 表示宿主（不绑定租户）的调用者。</summary>
    private SettingService CreateService(Guid? tenantId)
    {
        var tenant = new Mock<ICurrentTenant>();
        tenant.Setup(t => t.Id).Returns(tenantId);

        var repository = new EFCoreRepository<SettingTestDbContext, Setting, Guid>(DbContext, serviceProvider: ServiceProvider);
        var applicationOptions = new Mock<IOptionsMonitor<ApplicationOptions>>();
        applicationOptions.SetupGet(o => o.CurrentValue).Returns(new ApplicationOptions());

        return new SettingService(
            ServiceProvider,
            repository,
            applicationOptions.Object,
            Microsoft.Extensions.Options.Options.Create(new SettingEncryptionOptions()),
            new Mock<ICache>().Object,
            [],
            [],
            currentTenant: tenant.Object);
    }

    private static string[] Values(Result<IEnumerable<SettingDto>> result)
    {
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Select(d => d.Value).OrderBy(v => v).ToArray();
    }

    [Fact]
    public async Task TenantAdmin_DefaultList_SeesGlobalAndOwnTenantRowsOnly()
    {
        var service = CreateService(TenantA);

        var values = Values(await service.GetSettingsAsync());

        values.ShouldBe(["******", $"Global:-", $"Tenant:{TenantA}"]);
    }

    [Fact]
    public async Task HostAdmin_DefaultList_SeesGlobalRowsOnly()
    {
        // 宿主没有「本租户」：默认视图只有 Global，看别的租户要显式 scope=Tenant。
        var service = CreateService(null);

        Values(await service.GetSettingsAsync()).ShouldBe(["Global:-"]);
    }

    [Fact]
    public async Task TenantAdmin_ExplicitTenantScope_IsPinnedToTheCallersTenant()
    {
        var service = CreateService(TenantA);

        var values = Values(await service.GetSettingsAsync(scope: SettingScope.Tenant));

        values.ShouldBe(["******", $"Tenant:{TenantA}"]);
    }

    [Fact]
    public async Task TenantAdmin_AskingForAnotherTenant_IsRefusedNotSilentlyRewritten()
    {
        var service = CreateService(TenantA);

        var result = await service.GetSettingsAsync(scope: SettingScope.Tenant, scopeId: TenantB.ToString());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
    }

    [Fact]
    public async Task HostAdmin_TenantScope_ListsEveryTenant_OrTheOneAskedFor()
    {
        var service = CreateService(null);

        Values(await service.GetSettingsAsync(scope: SettingScope.Tenant))
            .ShouldBe(["******", $"Tenant:{TenantA}", $"Tenant:{TenantB}"]);
        Values(await service.GetSettingsAsync(scope: SettingScope.Tenant, scopeId: TenantB.ToString()))
            .ShouldBe([$"Tenant:{TenantB}"]);
    }

    [Fact]
    public async Task TenantAdmin_UserScope_NeedsAnExplicitUser()
    {
        // User 行没有租户列，租户内的调用者列不出「全部用户」，只能一次看一个明确的人。
        var service = CreateService(TenantA);

        var all = await service.GetSettingsAsync(scope: SettingScope.User);
        all.Succeeded.ShouldBeFalse();
        all.Code.ShouldBe(400);

        Values(await service.GetSettingsAsync(scope: SettingScope.User, scopeId: UserX.ToString()))
            .ShouldBe([$"User:{UserX}"]);
    }

    [Fact]
    public async Task HostAdmin_UserScope_ListsEveryUser()
    {
        var service = CreateService(null);

        Values(await service.GetSettingsAsync(scope: SettingScope.User))
            .ShouldBe([$"User:{UserX}", $"User:{UserY}"]);
    }

    [Fact]
    public async Task GroupFilter_StillApplies_OnTopOfScope()
    {
        var service = CreateService(TenantA);

        Values(await service.GetSettingsAsync(group: "B")).ShouldBeEmpty();
        Values(await service.GetSettingsAsync(group: "B", scope: SettingScope.User, scopeId: UserX.ToString()))
            .ShouldBe([$"User:{UserX}"]);
    }

    [Fact]
    public async Task GroupCounts_AgreeWithTheList()
    {
        var service = CreateService(TenantA);

        var groups = await service.GetSettingGroupsAsync();

        groups.Succeeded.ShouldBeTrue();
        var group = groups.Data!.ShouldHaveSingleItem();
        group.GroupName.ShouldBe("A");
        group.SettingCount.ShouldBe(3);   // Global + 本租户两行（含加密行），与默认列表一致

        var userGroups = await service.GetSettingGroupsAsync(SettingScope.User, UserX.ToString());
        userGroups.Data!.ShouldHaveSingleItem().GroupName.ShouldBe("B");
    }

    [Fact]
    public async Task TenantAdmin_ReadingAnotherTenantsRowById_Is404()
    {
        var service = CreateService(TenantA);
        var foreign = await DbContext.Set<Setting>().AsNoTracking()
            .SingleAsync(s => s.Scope == SettingScope.Tenant && s.ScopeId == TenantB.ToString());
        var own = await DbContext.Set<Setting>().AsNoTracking()
            .SingleAsync(s => s.Scope == SettingScope.Tenant && s.ScopeId == TenantA.ToString() && !s.IsEncrypted);

        (await service.GetSettingByIdAsync(foreign.Id)).Code.ShouldBe(404);
        (await service.UpdateSettingAsync(foreign.Id, new UpdateSettingDto { Value = "x" })).Code.ShouldBe(404);
        (await service.DeleteSettingAsync(foreign.Id)).Code.ShouldBe(404);

        (await service.GetSettingByIdAsync(own.Id)).Succeeded.ShouldBeTrue();
    }

    // ---- 写路径：创建与按作用域设值必须过同一道租户判定 ----
    // 09-04 那轮收口的是列表与按 id 的读 / 改 / 删；创建这条路漏了：租户 A 的管理员
    // POST admin/settings {scope: Tenant, scopeId: <B>} 会在租户 B 下植入一行，
    // TenantSettingProvider（优先级 200）随即对 B 的每个请求返回攻击者的值，
    // 而两边的读取面都看不见它（A 按 id 读是 404，B 的列表只有自己的行）。

    private static CreateSettingDto NewSetting(SettingScope scope, string? scopeId) => new()
    {
        Key = "Planted.Key",
        Value = "attacker",
        Group = "A",
        Scope = scope,
        ScopeId = scopeId
    };

    private Task<Setting?> PlantedRow() =>
        DbContext.Set<Setting>().AsNoTracking().SingleOrDefaultAsync(s => s.Key == "Planted.Key");

    [Fact]
    public async Task Create_TenantCallerNamingAnotherTenant_Returns403_AndWritesNothing()
    {
        var service = CreateService(TenantA);

        var result = await service.CreateSettingAsync(NewSetting(SettingScope.Tenant, TenantB.ToString()));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        (await PlantedRow()).ShouldBeNull();
    }

    [Fact]
    public async Task Create_TenantCallerWithoutScopeId_IsPinnedToTheCallersTenant()
    {
        // 缺省不是改写：没点名租户的 Tenant 行落在调用者自己的租户下。
        var service = CreateService(TenantA);

        var result = await service.CreateSettingAsync(NewSetting(SettingScope.Tenant, null));

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.ScopeId.ShouldBe(TenantA.ToString());
        (await PlantedRow())!.ScopeId.ShouldBe(TenantA.ToString());
    }

    [Fact]
    public async Task Create_TenantCaller_UserScopeOfAnotherUser_Returns403()
    {
        // User 行没有租户列，System 模块也核验不了目标用户属于哪个租户：租户内的调用者
        // 只能给自己写 User 作用域（失败关闭），给任何别人写都拒绝 —— UserSettingProvider
        // 的优先级最高（300），一行就能盖掉那个用户的全部分层配置。
        var service = CreateService(TenantA);

        var other = await service.CreateSettingAsync(NewSetting(SettingScope.User, UserX.ToString()));
        other.Succeeded.ShouldBeFalse();
        other.Code.ShouldBe(403);
        (await PlantedRow()).ShouldBeNull();

        var self = await service.CreateSettingAsync(NewSetting(SettingScope.User, TestHelper.DefaultTestUserId.ToString()));
        self.Succeeded.ShouldBeTrue(self.Message);
        (await PlantedRow())!.ScopeId.ShouldBe(TestHelper.DefaultTestUserId.ToString());
    }

    [Fact]
    public async Task Create_TenantCaller_GlobalScope_StillAllowed()
    {
        // 与 09-04 的口径一致：Global 行不属于任何租户，租户内照样能建（改 / 删也放行）。
        var service = CreateService(TenantA);

        var result = await service.CreateSettingAsync(NewSetting(SettingScope.Global, null));

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task Create_HostCaller_CanNameAnyTenantOrUser()
    {
        var service = CreateService(null);

        var tenant = await service.CreateSettingAsync(NewSetting(SettingScope.Tenant, TenantB.ToString()));
        tenant.Succeeded.ShouldBeTrue(tenant.Message);
        tenant.Data!.ScopeId.ShouldBe(TenantB.ToString());

        var user = await service.CreateSettingAsync(new CreateSettingDto
        {
            Key = "Planted.User", Value = "v", Group = "A", Scope = SettingScope.User, ScopeId = UserY.ToString()
        });
        user.Succeeded.ShouldBeTrue(user.Message);
    }

    [Fact]
    public async Task ScopedSetSetting_TenantCallerNamingAnotherTenant_Returns403_AndWritesNothing()
    {
        var service = CreateService(TenantA);

        var result = await service.SetSettingAsync("Planted.Key", "attacker", SettingScope.Tenant, TenantB.ToString());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        (await PlantedRow()).ShouldBeNull();
        // 既有的 B 行也没被碰
        (await DbContext.Set<Setting>().AsNoTracking()
            .SingleAsync(s => s.Key == "Site.Title" && s.ScopeId == TenantB.ToString())).Value.ShouldBe($"Tenant:{TenantB}");
    }

    [Fact]
    public async Task ScopedSetSetting_TenantCallerWithoutScopeId_IsPinnedToTheCallersTenant()
    {
        var service = CreateService(TenantA);

        var result = await service.SetSettingAsync("Planted.Key", "v", SettingScope.Tenant, null);

        result.Succeeded.ShouldBeTrue(result.Message);
        (await PlantedRow())!.ScopeId.ShouldBe(TenantA.ToString());
    }

    [Fact]
    public async Task ScopedSetSetting_TenantCaller_UserScopeOfAnotherUser_Returns403()
    {
        var service = CreateService(TenantA);

        var result = await service.SetSettingAsync("Ui.Theme", "attacker", SettingScope.User, UserX.ToString());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        (await DbContext.Set<Setting>().AsNoTracking()
            .SingleAsync(s => s.Key == "Ui.Theme" && s.ScopeId == UserX.ToString())).Value.ShouldBe($"User:{UserX}");
    }

    // ---- 按 id 的改 / 删：User 行要过与创建相同的本人判定 ----
    // 创建与按作用域设值拒绝给别人写 User 行，但同一行的 id 经列表可查
    // （GET admin/settings?scope=User&scopeId=<victim>），拿到 id 后 PUT / DELETE 仍然放行，
    // 「只允许写自己的用户」这条规则就只对受害者从没设过的键成立。

    private Task<Setting> UserRow(Guid userId) =>
        DbContext.Set<Setting>().AsNoTracking().SingleAsync(s => s.Scope == SettingScope.User && s.ScopeId == userId.ToString());

    [Fact]
    public async Task UpdateById_TenantCaller_UserRowOfAnotherUser_Returns403_AndLeavesTheRow()
    {
        var service = CreateService(TenantA);
        var victim = await UserRow(UserX);

        var result = await service.UpdateSettingAsync(victim.Id, new UpdateSettingDto { Value = "attacker" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        (await UserRow(UserX)).Value.ShouldBe($"User:{UserX}");
    }

    [Fact]
    public async Task DeleteById_TenantCaller_UserRowOfAnotherUser_Returns403_AndLeavesTheRow()
    {
        var service = CreateService(TenantA);
        var victim = await UserRow(UserX);

        (await service.DeleteSettingAsync(victim.Id)).Code.ShouldBe(403);
        (await service.DeleteSettingsAsync([victim.Id])).Code.ShouldBe(403);

        (await UserRow(UserX)).Value.ShouldBe($"User:{UserX}");
    }

    [Fact]
    public async Task UpdateAndDeleteById_TenantCaller_OwnUserRow_Succeeds()
    {
        var service = CreateService(TenantA);
        (await service.CreateSettingAsync(NewSetting(SettingScope.User, TestHelper.DefaultTestUserId.ToString()))).Succeeded.ShouldBeTrue();
        var own = await UserRow(TestHelper.DefaultTestUserId);

        var updated = await service.UpdateSettingAsync(own.Id, new UpdateSettingDto { Value = "mine" });
        updated.Succeeded.ShouldBeTrue(updated.Message);
        (await UserRow(TestHelper.DefaultTestUserId)).Value.ShouldBe("mine");

        var deleted = await service.DeleteSettingAsync(own.Id);
        deleted.Succeeded.ShouldBeTrue(deleted.Message);
        (await PlantedRow()).ShouldBeNull();
    }

    [Fact]
    public async Task UpdateAndDeleteById_HostCaller_AnyUserRow_Succeeds()
    {
        var service = CreateService(null);
        var row = await UserRow(UserY);

        var updated = await service.UpdateSettingAsync(row.Id, new UpdateSettingDto { Value = "host" });
        updated.Succeeded.ShouldBeTrue(updated.Message);

        var deleted = await service.DeleteSettingAsync(row.Id);
        deleted.Succeeded.ShouldBeTrue(deleted.Message);
    }

    [Fact]
    public async Task ScopedSetSetting_RefusesToOverwriteAnEncryptedRowWithPlaintext()
    {
        // 与另外两条写路径同一道防护：明文盖掉密文而 IsEncrypted 仍为 true，之后每次读取都拿明文去解密。
        var service = CreateService(TenantA);

        var result = await service.SetSettingAsync("Mail.Password", "plain", SettingScope.Tenant, TenantA.ToString());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        (await DbContext.Set<Setting>().AsNoTracking().SingleAsync(s => s.Key == "Mail.Password")).Value.ShouldBe("cipher");
    }
}
