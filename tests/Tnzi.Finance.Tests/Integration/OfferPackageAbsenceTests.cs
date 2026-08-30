using Tnzi.Finance.Permissions;
using Tnzi.Security.Authorization;
using Tnzi.Settings;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 未加载 <c>Tnzi.Finance.Offers</c> 时，会计内核退化成什么。
/// </summary>
/// <remarks>
/// <para>
/// 本测试项目<b>刻意不引用</b> <c>Tnzi.Finance.Offers</c>（它引用 Banking / Documents /
/// Recurring，唯独没有它），所以进程里根本没有 <see cref="IMasterDataUsageProvider"/> 的实现，
/// 也没有那四张表 —— 这正是消费方少加载一个包时的现场，不需要额外搭夹具去模拟。
/// </para>
/// <para>
/// ★ 要守住的命题是<b>「少一点能力，绝不是错的行为」</b>：删除守卫少问一个问题（少拒绝），
/// 而它自己那几项检查一条都不能少（不多放行）。反过来的形态才是灾难 —— 一个"缺了实现就放宽"
/// 的守卫，在最需要它的部署上恰好失效，而日志、返回值、界面全都正常。
/// 与 <c>IJournalLineHoldProvider</c> 的缺省方向是同一条铁律。
/// </para>
/// </remarks>
public class OfferPackageAbsenceTests : FinanceIntegrationTestBase
{
    /// <summary>
    /// 一个实现都没注册时，主数据删除守卫回到"只有会计单据算数"，而且**照旧拒绝**它自己认得的引用。
    /// </summary>
    [Fact]
    public async Task WithoutTheOffersPackage_MasterDataGuardsStillRefuseTheirOwnReferences()
    {
        // 前提：容器里真的一个 IMasterDataUsageProvider 都没有。
        // 这一句是本套测试的锚：哪天有人在父模块里注册了一个默认实现，
        // 下面两条断言测的就不再是"缺席"了。
        ServiceProvider.GetServices<IMasterDataUsageProvider>().ShouldBeEmpty();

        await SeedCoaAsync();

        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = "Northwind Traders", Currency = "USD" }));
        customer.Succeeded.ShouldBeTrue(customer.Message);

        var revenue = await AccountIdByCodeAsync("4100");
        var invoice = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customer.Data!.Id,
            DocDate = DateTime.UtcNow.Date,
            Currency = "USD",
            Lines = [new CreateInvoiceLineDto { AccountId = revenue, Quantity = 1, UnitPrice = 100m }]
        }));
        invoice.Succeeded.ShouldBeTrue(invoice.Message);

        // 核心自己那几项检查与引入契约之前逐字一致：被发票引用的客户仍然删不掉。
        var deleted = await InScopeAsync<ICustomerService, Result>(s => s.DeleteAsync(customer.Data!.Id));

        deleted.Succeeded.ShouldBeFalse();
        deleted.Code.ShouldBe(409);
        deleted.Message.ShouldNotBeNull();
        deleted.Message.ShouldContain("invoices");
    }

    /// <summary>
    /// 没有任何会计单据引用它时，删除放行 —— 这就是"少问一个问题"的可观察形态。
    /// </summary>
    /// <remarks>
    /// 加载了要约模块的宿主上，同一条客户若挂着一张报价单会被拒绝（见
    /// <c>Tnzi.Finance.Offers.Tests</c> 的 <c>MasterDataUsageTests</c>）。两边合起来才说清
    /// 这个契约的语义：**它只会增加拒绝**。
    /// </remarks>
    [Fact]
    public async Task WithoutTheOffersPackage_AnUnreferencedCustomerIsDeletable()
    {
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = "Contoso Ltd", Currency = "USD" }));
        customer.Succeeded.ShouldBeTrue(customer.Message);

        var deleted = await InScopeAsync<ICustomerService, Result>(s => s.DeleteAsync(customer.Data!.Id));

        deleted.Succeeded.ShouldBeTrue(deleted.Message);
    }

    /// <summary>
    /// 不加载本包的宿主永远不会 seed 报价单 / 采购订单的 8 个权限码。
    /// </summary>
    /// <remarks>
    /// 权限矩阵里多出两块永远授不出去也永远打不开的功能面，比少两行更糟：
    /// 管理员会以为授了权就能用。码串本身一字未改（只是换了个声明者），
    /// 见 <c>Tnzi.PermissionCatalogue.Tests</c> 的目录 pact。
    /// </remarks>
    [Fact]
    public void WithoutTheOffersPackage_TheOfferPermissionCodesAreNotDeclared()
    {
        var context = new PermissionDefinitionContext();
        new FinancePermissions().Define(context);

        context.Permissions.Keys.ShouldNotContain("finance.estimate.view");
        context.Permissions.Keys.ShouldNotContain("finance.purchaseOrder.view");
        // 与此同时会计单据那套码一个不少：拆走的只是要约那两套。
        context.Permissions.Keys.ShouldContain("finance.document.create");
    }

    /// <summary>
    /// 号段前缀随单据走：不加载本包的宿主在配置中心里看不到这两个设置项。
    /// </summary>
    /// <remarks>
    /// 渲染出来却控制不了任何东西的设置项，比没有这个设置项更糟 —— 运维改了它、保存成功、
    /// 什么也没发生。键路径（<c>Finance:EstimateNumberPrefix</c>）与分组位置在加载本包时一字未变。
    /// </remarks>
    [Fact]
    public void WithoutTheOffersPackage_TheOfferNumberPrefixesAreNotSurfacedAsSettings()
    {
        var group = RuntimeSettingMetadataExtractor.Extract(typeof(FinanceOptions));

        group.ShouldNotBeNull();
        group.Fields.Select(f => f.Key).ShouldNotContain("Finance:EstimateNumberPrefix");
        group.Fields.Select(f => f.Key).ShouldNotContain("Finance:PurchaseOrderNumberPrefix");
        // 同组里核心自己的号段前缀照旧在。
        group.Fields.Select(f => f.Key).ShouldContain("Finance:PaymentNumberPrefix");
    }
}
