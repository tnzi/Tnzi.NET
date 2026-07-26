namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 账龄分桶切分点参数化（P4-8）：30/60/90 是惯例不是法律。
/// </summary>
public class AgingBucketConfigTests : FinanceIntegrationTestBase
{
    /// <summary>按周结算的行业常用 7/14/21。</summary>
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<FinanceOptions>(o => o.AgingBucketDays = [7, 14, 21]);
    }

    [Fact]
    public async Task Aging_UsesTheConfiguredCutOffs()
    {
        await SeedCoaAsync();
        var today = DateTime.UtcNow.Date;

        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = "Weekly Terms Ltd", Currency = "USD" }));
        var revenue = await AccountIdByCodeAsync("4100");

        // 逾期 10 天：默认口径落 1-30 桶，7/14/21 口径落第二桶（8-14）。
        var draft = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customer.Data!.Id,
            DocDate = today.AddDays(-20),
            DueDate = today.AddDays(-10),
            Currency = "USD",
            Lines = [new CreateInvoiceLineDto { AccountId = revenue, Quantity = 1, UnitPrice = 250m }]
        }));
        await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.PostAsync(draft.Data!.Id));

        var aging = await InScopeAsync<IFinancialReportService, Result<AgingReportDto>>(s => s.GetArAgingAsync(today));

        var row = aging.Data!.Rows.Single(r => r.PartyId == customer.Data.Id);
        row.Days1To30.ShouldBe(0m);
        row.Days31To60.ShouldBe(250m);
    }
}

/// <summary>
/// 账龄切分点走**真实的配置绑定**，而不是代码里直接赋值。
/// </summary>
/// <remarks>
/// ★.NET 的配置绑定对数组是**追加**语义：先复制属性上现有的元素，再把绑定到的接在
/// 后面。所以 <c>AgingBucketDays</c> 的默认值一旦预置成 [30,60,90]，任何配了它的部署
/// 都会绑成六个元素、随即被"必须恰好三个"的校验挡在启动之外，而错误信息指向操作员
/// 那份（正确的）配置。上面那个测试用 <c>services.Configure</c> 直接赋值，绕过了绑定，
/// 因此**抓不到**这颗雷。
/// </remarks>
public class AgingBucketBindingTests
{
    private static FinanceOptions Bind(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = new FinanceOptions();
        configuration.GetSection("Finance").Bind(options);
        return options;
    }

    [Fact]
    public void ConfiguredCutOffs_BindToExactlyThree_AndPassValidation()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Finance:AgingBucketDays:0"] = "7",
            ["Finance:AgingBucketDays:1"] = "14",
            ["Finance:AgingBucketDays:2"] = "21",
        });

        options.AgingBucketDays.ShouldBe([7, 14, 21]);
        options.ResolveAgingBucketDays().ShouldBe([7, 14, 21]);

        new FinanceOptionsValidator().Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void UnconfiguredCutOffs_FallBackTo30_60_90()
    {
        var options = Bind([]);

        options.AgingBucketDays.ShouldBeEmpty();
        options.ResolveAgingBucketDays().ShouldBe([30, 60, 90]);
    }
}
