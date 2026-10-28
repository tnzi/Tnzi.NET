using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tnzi.Finance.Documents.Metadata;
using Tnzi.MultiTenancy;
using Tnzi.Security.Claims;
using Tnzi.Template.Entities;
using Tnzi.Template.Services;
using TemplateEntity = Tnzi.Template.Entities.Template;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 多租户开启时，出厂支票版式对租户照样可用。
/// </summary>
/// <remarks>
/// 启动播种跑在根容器里、没有租户上下文，写下的是宿主行（<c>TenantId</c> 为空）；
/// 租户过滤器是严格等值，于是租户看不见它们。此前渲染器按名取模板只认库里的行，
/// 多租户下每个租户的打印、预览、样张、重打全部 404。本组走真实的播种入口、真实的
/// 模板存储与 Razor 渲染、真实的租户过滤器（<c>UseTnziMultiTenancy(true)</c>）。
/// </remarks>
public class CheckTemplateMultiTenancyTests : IDisposable
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private const string TenantMarker = "tenant-calibrated-layout-marker";

    private readonly SqliteConnection _connection;

    public CheckTemplateMultiTenancyTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var provider = BuildProvider(tenantId: null);
        provider.GetRequiredService<TemplateStoreTestDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task Tenant_RendersABuiltInLayout_SeededOnlyAsAHostRow()
    {
        await SeedAtHostAsync();

        var result = await RenderAsync(TenantA);

        result.Succeeded.ShouldBeTrue(result.Message);
        Encoding.UTF8.GetString(result.Data!).ShouldContain("Northwind Supplies");
    }

    [Fact]
    public async Task Tenant_OwnRowOfTheSameName_WinsOverTheShippedBody()
    {
        await SeedAtHostAsync();
        await InsertRowAsync(TenantA, $"<html>{TenantMarker} @Model.Checks.Count</html>", isActive: true);

        var result = await RenderAsync(TenantA);

        result.Succeeded.ShouldBeTrue(result.Message);
        Encoding.UTF8.GetString(result.Data!).ShouldContain(TenantMarker);
    }

    /// <summary>停用是有人特意关掉了这个版式：兜底只在「根本没有行」时发生，不能绕过停用。</summary>
    [Fact]
    public async Task Tenant_DeactivatedRow_IsNotBypassedByTheShippedBody()
    {
        await SeedAtHostAsync();
        await InsertRowAsync(TenantA, $"<html>{TenantMarker}</html>", isActive: false);

        var result = await RenderAsync(TenantA);

        result.Succeeded.ShouldBeFalse("a layout the tenant switched off must stay off");
        result.Code.ShouldBe(404);
    }

    /// <summary>宿主侧（单租户部署的形态）同样不绕过停用，兜底没有改变既有语义。</summary>
    [Fact]
    public async Task Host_DeactivatedSeededRow_StillFails()
    {
        await SeedAtHostAsync();
        await using (var provider = BuildProvider(tenantId: null))
        {
            var db = provider.GetRequiredService<TemplateStoreTestDbContext>();
            (await db.Set<TemplateEntity>()
                .Where(t => t.TemplateName == CheckTemplates.Cpa006Canada && t.Module == CheckTemplates.Module)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive, false))).ShouldBe(1);
        }

        var result = await RenderAsync(tenantId: null);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    /// <summary>自建模板（不在出厂目录里）缺失是真实的配置错误，不兜底。</summary>
    [Fact]
    public async Task Tenant_MissingCustomTemplate_StillFails()
    {
        await SeedAtHostAsync();

        var result = await RenderAsync(TenantA, templateName: "shop-custom-cheque");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    // ── 夹具 ─────────────────────────────────────────────────

    private async Task SeedAtHostAsync()
    {
        await using var provider = BuildProvider(tenantId: null);
        await new CheckTemplateSeeder().ExecuteAsync(provider);

        // 前提：播种行确实是宿主行，且租户侧确实看不见它们。
        var hostRows = await provider.GetRequiredService<IReadOnlyRepository<TemplateEntity, Guid>>().AsNoTracking().CountAsync();
        hostRows.ShouldBe(BuiltInCheckTemplates.All.Count);
        await using var tenant = BuildProvider(TenantA);
        (await tenant.GetRequiredService<IReadOnlyRepository<TemplateEntity, Guid>>().AsNoTracking().CountAsync())
            .ShouldBe(0, "premise: the tenant filter hides the host rows");
    }

    private async Task InsertRowAsync(Guid tenantId, string body, bool isActive)
    {
        await using var provider = BuildProvider(tenantId);
        var repository = provider.GetRequiredService<IRepository<TemplateEntity, Guid>>();
        await repository.InsertAsync(new TemplateEntity
        {
            TemplateName = CheckTemplates.Cpa006Canada,
            Module = CheckTemplates.Module,
            Category = CheckTemplates.Category,
            Type = TemplateType.Print,
            ContentTemplate = body,
            SubjectTemplate = string.Empty,
            IsActive = isActive,
            TenantId = tenantId
        });
        await repository.SaveChangesAsync();
    }

    private async Task<Result<byte[]>> RenderAsync(Guid? tenantId, string? templateName = null)
    {
        await using var provider = BuildProvider(tenantId);
        await using var scope = provider.CreateAsyncScope();
        var renderer = scope.ServiceProvider.GetRequiredService<TemplateCheckRenderer>();
        return await renderer.RenderAsync(new CheckRenderRequest
        {
            Layout = CheckLayout.Voucher,
            StockType = CheckStockType.PrePrinted,
            Scheme = BankNumberScheme.CaEft,
            BankName = "Bank of the North",
            AccountName = "Operating",
            TemplateName = templateName ?? CheckTemplates.Cpa006Canada,
            Checks =
            [
                new CheckRenderItem
                {
                    CheckNumber = 1001,
                    PayeeName = "Northwind Supplies",
                    Amount = 1234.56m,
                    Currency = "CAD",
                    AmountInWords = "One Thousand Two Hundred Thirty-Four and 56/100 Dollars",
                    IssueDate = new DateTime(2026, 3, 17, 0, 0, 0, DateTimeKind.Utc)
                }
            ]
        });
    }

    private ServiceProvider BuildProvider(Guid? tenantId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.Id).Returns(TestHelper.DefaultTestUserId);
        currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        currentUser.Setup(u => u.TenantId).Returns(tenantId);
        services.AddScoped(_ => currentUser.Object);

        var currentTenant = new Mock<ICurrentTenant>();
        currentTenant.Setup(t => t.Id).Returns(tenantId);
        currentTenant.Setup(t => t.IsAvailable).Returns(tenantId.HasValue);
        services.AddScoped(_ => currentTenant.Object);

        services.AddSingleton(new TemplateWriteFault());
        services.AddDbContext<TemplateStoreTestDbContext>(options =>
        {
            options.UseSqlite(_connection);
            options.UseTnziMultiTenancy(true);
        });
        services.AddScoped<IRepository<TemplateEntity, Guid>>(sp =>
            new EFCoreRepository<TemplateStoreTestDbContext, TemplateEntity, Guid>(
                sp.GetRequiredService<TemplateStoreTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IReadOnlyRepository<TemplateEntity, Guid>>(sp =>
            sp.GetRequiredService<IRepository<TemplateEntity, Guid>>());

        services.AddSingleton<ITemplateEngine>(_ => new RazorTemplateEngine(
            new OptionsWrapper<TemplateOptions>(new TemplateOptions { EnableCache = false }),
            NullLogger<RazorTemplateEngine>.Instance,
            new MemoryCache(new MemoryCacheOptions())));
        services.AddScoped<ITemplateStoreService, TemplateStoreService>();
        services.AddScoped<ITemplateRenderService, TemplateRenderService>();
        services.AddScoped<TemplateCheckRenderer>();

        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
