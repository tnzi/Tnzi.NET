using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 管理端「新建计划」：表单发来的请求体能绑定，没填币种时落成这台宿主的默认币种。
/// </summary>
/// <remarks>
/// 消费方实测：新建计划恒 400「The dto field is required.」/「$.id: could not be converted to System.Guid」——
/// 表单带着 <c>"id": ""</c>，一个字段绑定失败整个请求体就绑不上；绑上了的计划又默认是 USD 而不是部署配的 CAD。
/// </remarks>
public class PlanCreateFromAdminFormTests : SubscriptionsIntegrationTestBase
{
    // 与 ASP.NET Core 的请求体绑定同口径：Web 默认值 + 字符串枚举
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<PaymentOptions>(o => o.DefaultCurrency = "CAD");
    }

    [Fact]
    public void AnEmptyIdCannotBind_WhichIsWhyTheFormMustOmitIt()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<SubscriptionPlanDto>("""{"id":"","planCode":"P","planName":"P"}""", Wire));
    }

    [Fact]
    public async Task ThePlanFormsCreateBody_BindsAndTakesTheDefaultCurrency()
    {
        // 表单新建时发出的形状（ui-admin toPlanWrite）：没有 id，币种未填
        var body = JsonSerializer.Deserialize<SubscriptionPlanDto>("""
            {"planCode":"FIRM-M","productCode":null,"planName":"Firm","description":null,"price":100,"currency":"",
             "cycleType":"Month","cycleValue":1,"allowTrial":false,"trialDays":0,"trialDiscount":null,"sortOrder":0,"isActive":true}
            """, Wire)!;

        var created = await InScopeAsync<ISubscriptionService, Result<SubscriptionPlanDto>>(svc => svc.CreatePlanAsync(body));

        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Currency.ShouldBe("CAD");
        (await ReloadAsync<SubscriptionPlan>(created.Data.Id))!.Currency.ShouldBe("CAD");
    }

    [Fact]
    public async Task ACurrencyLeftOutOfTheBody_IsTheDefaultToo_AndTheFormPrefillReadsIt()
    {
        var body = JsonSerializer.Deserialize<SubscriptionPlanDto>("""{"planCode":"FIRM-Y","planName":"Firm","price":1000,"cycleType":"Year","cycleValue":1,"isActive":true}""", Wire)!;

        var created = await InScopeAsync<ISubscriptionService, Result<SubscriptionPlanDto>>(svc => svc.CreatePlanAsync(body));

        created.Data!.Currency.ShouldBe("CAD", "a missing field used to bind as USD");
        (await InScopeAsync<ISubscriptionService, Result<SubscriptionPlanDefaultsDto>>(svc => Task.FromResult(svc.GetPlanDefaults()))).Data!.Currency.ShouldBe("CAD");
    }

    [Fact]
    public async Task StorageTerms_AreSavedOnCreateAndUpdate_AndNegativeValuesAreRefused()
    {
        var body = JsonSerializer.Deserialize<SubscriptionPlanDto>("""
            {"planCode":"FIRM-S","planName":"Firm","price":299,"currency":"CAD","cycleType":"Month","cycleValue":1,"isActive":true,
             "includedStorageBytes":268435456000,"storageOveragePricePerGbMonth":0.20}
            """, Wire)!;

        var created = await InScopeAsync<ISubscriptionService, Result<SubscriptionPlanDto>>(svc => svc.CreatePlanAsync(body));
        created.Succeeded.ShouldBeTrue(created.Message);
        var saved = (await ReloadAsync<SubscriptionPlan>(created.Data!.Id))!;
        saved.IncludedStorageBytes.ShouldBe(268435456000L);
        saved.StorageOveragePricePerGbMonth.ShouldBe(0.20m);

        body.Id = created.Data.Id;
        body.IncludedStorageBytes = null;
        body.StorageOveragePricePerGbMonth = null;
        (await InScopeAsync<ISubscriptionService, Result>(svc => svc.UpdatePlanAsync(created.Data.Id, body))).Succeeded.ShouldBeTrue();
        (await ReloadAsync<SubscriptionPlan>(created.Data.Id))!.IncludedStorageBytes.ShouldBeNull("clearing the allowance turns metering off");

        body.IncludedStorageBytes = -1;
        (await InScopeAsync<ISubscriptionService, Result>(svc => svc.UpdatePlanAsync(created.Data.Id, body))).Code.ShouldBe(400);
    }

    [Fact]
    public async Task AnExplicitCurrency_IsKept()
    {
        var body = new SubscriptionPlanDto { PlanCode = "FIRM-U", PlanName = "Firm", Price = 80, Currency = "usd", CycleType = BillingCycleType.Month, CycleValue = 1, IsActive = true };

        var created = await InScopeAsync<ISubscriptionService, Result<SubscriptionPlanDto>>(svc => svc.CreatePlanAsync(body));

        created.Data!.Currency.ShouldBe("USD");
    }
}
