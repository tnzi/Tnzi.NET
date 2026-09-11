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
