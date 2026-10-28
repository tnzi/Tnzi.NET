namespace Tnzi.Finance.Offers.Tests.Integration;

/// <summary>
/// 报价单 / 采购订单的文本字段超长时答 400 并点名字段，不留给数据库。
/// </summary>
/// <remarks>
/// 测试库是 SQLite，不检查列宽：超长值在这里「保存成功」，在 SQL Server / PostgreSQL 上是一次不点名字段的截断 500。
/// 所以这些用例断言的是服务层的拒绝本身（与刚好卡在上限的值仍能保存的对照）。
/// </remarks>
public class OfferFieldLengthTests : OfferIntegrationTestBase
{
    private async Task<Guid> CustomerAsync()
    {
        var r = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = "Acme Supplies Ltd", Currency = "USD", PaymentTermsDays = 30 }));
        r.Succeeded.ShouldBeTrue(r.Message);
        return r.Data!.Id;
    }

    private async Task<Guid> VendorAsync()
    {
        var r = await InScopeAsync<IVendorService, Result<VendorDto>>(
            s => s.CreateAsync(new CreateVendorDto { Name = "Northwind Supply", Currency = "USD", PaymentTermsDays = 30 }));
        r.Succeeded.ShouldBeTrue(r.Message);
        return r.Data!.Id;
    }

    private async Task<CreateEstimateDto> EstimateInputAsync(Action<CreateEstimateDto>? change = null)
    {
        var input = new CreateEstimateDto
        {
            CustomerId = await CustomerAsync(),
            DocDate = DateTime.UtcNow.Date,
            Currency = "USD",
            Lines = [new CreateOfferLineDto { AccountId = await AccountIdByCodeAsync("4100"), Description = "Design", Quantity = 1, UnitPrice = 100m }]
        };
        change?.Invoke(input);
        return input;
    }

    private async Task<CreatePurchaseOrderDto> OrderInputAsync(Action<CreatePurchaseOrderDto>? change = null)
    {
        var input = new CreatePurchaseOrderDto
        {
            VendorId = await VendorAsync(),
            DocDate = DateTime.UtcNow.Date,
            Currency = "USD",
            Lines = [new CreateOfferLineDto { AccountId = await AccountIdByCodeAsync("5200"), Description = "Chair", Quantity = 1, UnitPrice = 50m }]
        };
        change?.Invoke(input);
        return input;
    }

    private Task<Result<EstimateDto>> CreateEstimateAsync(CreateEstimateDto input)
        => InScopeAsync<IEstimateService, Result<EstimateDto>>(s => s.CreateDraftAsync(input));

    private Task<Result<PurchaseOrderDto>> CreateOrderAsync(CreatePurchaseOrderDto input)
        => InScopeAsync<IPurchaseOrderService, Result<PurchaseOrderDto>>(s => s.CreateDraftAsync(input));

    private static void ShouldBeRejected<T>(Result<T> result, string field)
    {
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain(field);
    }

    [Theory]
    [InlineData("memo")]
    [InlineData("internal note")]
    [InlineData("currency code")]
    [InlineData("Line 1: the description")]
    public async Task An_oversized_estimate_field_is_refused_by_name(string field)
    {
        await SeedCoaAsync();
        var input = await EstimateInputAsync(i =>
        {
            switch (field)
            {
                case "memo": i.Memo = new string('m', 501); break;
                case "internal note": i.InternalNote = new string('n', 1001); break;
                case "currency code": i.Currency = "ABCDEFGHI"; break;
                default: i.Lines[0].Description = new string('d', 501); break;
            }
        });

        ShouldBeRejected(await CreateEstimateAsync(input), field);
    }

    [Theory]
    [InlineData("memo")]
    [InlineData("internal note")]
    [InlineData("ship-to address")]
    [InlineData("Line 1: the description")]
    public async Task An_oversized_purchase_order_field_is_refused_by_name(string field)
    {
        await SeedCoaAsync();
        var input = await OrderInputAsync(i =>
        {
            switch (field)
            {
                case "memo": i.Memo = new string('m', 501); break;
                case "internal note": i.InternalNote = new string('n', 1001); break;
                case "ship-to address": i.ShipTo = new string('s', 501); break;
                default: i.Lines[0].Description = new string('d', 501); break;
            }
        });

        ShouldBeRejected(await CreateOrderAsync(input), field);
    }

    /// <summary>编辑走同一道校验，被拒绝的编辑不改动草稿。</summary>
    [Fact]
    public async Task An_oversized_edit_is_refused_and_leaves_the_draft_unchanged()
    {
        await SeedCoaAsync();
        var created = await CreateOrderAsync(await OrderInputAsync(i => i.ShipTo = "12 King St W"));
        created.Succeeded.ShouldBeTrue(created.Message);

        var edit = await OrderInputAsync(i => i.ShipTo = new string('s', 501));
        ShouldBeRejected(
            await InScopeAsync<IPurchaseOrderService, Result<PurchaseOrderDto>>(s => s.UpdateAsync(created.Data!.Id, edit)),
            "ship-to address");

        var reloaded = await InScopeAsync<IPurchaseOrderService, Result<PurchaseOrderDto>>(s => s.GetAsync(created.Data!.Id));
        reloaded.Data!.ShipTo.ShouldBe("12 King St W");
    }

    /// <summary>对照：刚好卡在列宽上的值照常保存。</summary>
    [Fact]
    public async Task Values_exactly_at_the_column_width_are_accepted()
    {
        await SeedCoaAsync();
        var estimate = await CreateEstimateAsync(await EstimateInputAsync(i =>
        {
            i.Memo = new string('m', 500);
            i.InternalNote = new string('n', 1000);
            i.Lines[0].Description = new string('d', 500);
        }));
        estimate.Succeeded.ShouldBeTrue(estimate.Message);

        var order = await CreateOrderAsync(await OrderInputAsync(i => i.ShipTo = new string('s', 500)));
        order.Succeeded.ShouldBeTrue(order.Message);
    }

    /// <summary>
    /// 行描述缺省时从目录项抄过来：目录项的描述列更宽（1000），抄过来的那一份收进单据行的列宽，原文留在目录项上。
    /// </summary>
    [Fact]
    public async Task A_long_catalog_description_copied_onto_a_line_is_fitted_to_the_line_width()
    {
        await SeedCoaAsync();
        var revenue = await AccountIdByCodeAsync("4100");
        var item = await InScopeAsync<IItemService, Result<ItemDto>>(s => s.CreateAsync(new CreateItemDto
        {
            Name = "Retainer",
            Description = new string('c', 1000),
            SalesPrice = 100m,
            IncomeAccountId = revenue,
        }));
        item.Succeeded.ShouldBeTrue(item.Message);

        var estimate = await CreateEstimateAsync(await EstimateInputAsync(i =>
            i.Lines = [new CreateOfferLineDto { ItemId = item.Data!.Id, AccountId = revenue, Quantity = 1, UnitPrice = 100m }]));

        estimate.Succeeded.ShouldBeTrue(estimate.Message);
        estimate.Data!.Lines.Single().Description!.Length.ShouldBe(500);
    }
}
