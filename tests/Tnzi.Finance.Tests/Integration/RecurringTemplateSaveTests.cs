using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 模板保存：能在保存时判定的错误当场 400，头与行要么一起写进去、要么一起不写，删除只看真的生成过的期次。
/// </summary>
/// <remarks>
/// <para>
/// 行与排期字段此前只在到期生成时才被检查：保存返回 200、预览一切正常，到期后每一期落一行 Failed，
/// 重试耗尽后这一期不再尝试，单据从未开出。
/// </para>
/// <para>
/// 行是硬删重建：头先提交、旧行删掉再提交、新行再提交，三次各自落库。新行写入失败（在 SQL Server /
/// PostgreSQL 上一条超宽的描述就够了）留下一条 Active 而零行的模板，原来的价格也没了。用 SaveChanges
/// 拦截器在「有新行要插入」的那一次提交上注入失败。
/// </para>
/// </remarks>
public class RecurringTemplateSaveTests : FinanceIntegrationTestBase
{
    private readonly FailLineInsertsInterceptor _failLineInserts = new();

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(_failLineInserts);

    private Guid _customerId;
    private Guid _vendorId;
    private Guid _revenueId;
    private Guid _expenseAccountId;
    private Guid _bankId;

    private async Task SeedAsync()
    {
        await SeedCoaAsync();
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = "Template Customer", Currency = "USD" }));
        customer.Succeeded.ShouldBeTrue(customer.Message);
        _customerId = customer.Data!.Id;
        var vendor = await InScopeAsync<IVendorService, Result<VendorDto>>(
            s => s.CreateAsync(new CreateVendorDto { Name = "Template Vendor", Currency = "USD" }));
        vendor.Succeeded.ShouldBeTrue(vendor.Message);
        _vendorId = vendor.Data!.Id;
        _revenueId = await AccountIdByCodeAsync("4100");
        _expenseAccountId = await AccountIdByCodeAsync("5200");
        _bankId = await AccountIdByCodeAsync("1120");
    }

    private CreateRecurringDocumentDto Invoice(params CreateRecurringLineDto[] lines) => new()
    {
        Name = "Monthly retainer",
        Kind = RecurringDocKind.Invoice,
        PartyId = _customerId,
        Currency = "USD",
        Frequency = RecurrenceFrequency.Monthly,
        StartDate = new DateTime(2026, 1, 1),
        Lines = lines.Length > 0 ? [.. lines] : [new CreateRecurringLineDto { AccountId = _revenueId, Quantity = 1, UnitPrice = 500m }],
    };

    private CreateRecurringDocumentDto Expense(params CreateRecurringLineDto[] lines) => new()
    {
        Name = "Bank fees",
        Kind = RecurringDocKind.Expense,
        PartyId = _vendorId,
        PaidFromAccountId = _bankId,
        Currency = "USD",
        Frequency = RecurrenceFrequency.Monthly,
        StartDate = new DateTime(2026, 1, 1),
        Lines = [.. lines],
    };

    private Task<Result<RecurringDocumentDto>> CreateAsync(CreateRecurringDocumentDto input)
        => InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.CreateAsync(input));

    private Task<int> TemplateCountAsync()
        => InScopeAsync<IRepository<RecurringDocument, Guid>, int>(repo => repo.AsQueryable().IgnoreQueryFilters().CountAsync());

    private async Task ShouldBeRejectedAsync(CreateRecurringDocumentDto input, string expectedFragment)
    {
        var result = await CreateAsync(input);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain(expectedFragment);
        (await TemplateCountAsync()).ShouldBe(0);
    }

    // ── 保存时校验 ─────────────────────────────────────────

    [Fact]
    public async Task Create_ExpenseLineWithoutAnAccount_Rejected400()
    {
        await SeedAsync();
        await ShouldBeRejectedAsync(Expense(new CreateRecurringLineDto { Quantity = 1, UnitPrice = 25m }), "no expense account");
    }

    [Fact]
    public async Task Create_ExpenseLineWithAZeroAmount_Rejected400()
    {
        await SeedAsync();
        await ShouldBeRejectedAsync(
            Expense(new CreateRecurringLineDto { AccountId = _expenseAccountId, Quantity = 1, UnitPrice = 0m }), "amount greater than zero");
    }

    [Fact]
    public async Task Create_LineWithANonPositiveQuantity_Rejected400()
    {
        await SeedAsync();
        await ShouldBeRejectedAsync(Invoice(new CreateRecurringLineDto { AccountId = _revenueId, Quantity = 0, UnitPrice = 10m }), "quantity");
    }

    [Fact]
    public async Task Create_LineWithANegativeUnitPrice_Rejected400()
    {
        await SeedAsync();
        await ShouldBeRejectedAsync(Invoice(new CreateRecurringLineDto { AccountId = _revenueId, Quantity = 1, UnitPrice = -10m }), "unit price");
    }

    [Fact]
    public async Task Create_LineWithAnUnknownItem_Rejected400()
    {
        await SeedAsync();
        await ShouldBeRejectedAsync(
            Invoice(new CreateRecurringLineDto { ItemId = Guid.NewGuid(), Quantity = 1, UnitPrice = 10m }), "catalog items");
    }

    [Fact]
    public async Task Create_LineWithAnUnknownAccount_Rejected400()
    {
        await SeedAsync();
        await ShouldBeRejectedAsync(Invoice(new CreateRecurringLineDto { AccountId = Guid.NewGuid(), Quantity = 1, UnitPrice = 10m }), "accounts");
    }

    [Fact]
    public async Task Create_LineWithAnUnknownTaxCode_Rejected400()
    {
        await SeedAsync();
        await ShouldBeRejectedAsync(
            Invoice(new CreateRecurringLineDto { AccountId = _revenueId, Quantity = 1, UnitPrice = 10m, TaxCodeId = Guid.NewGuid() }), "tax codes");
    }

    [Fact]
    public async Task Create_NegativeDueDays_Rejected400()
    {
        await SeedAsync();
        var input = Invoice();
        input.DueDays = -30;
        await ShouldBeRejectedAsync(input, "Due days");
    }

    [Fact]
    public async Task Create_OverlongLineDescription_Rejected400()
    {
        await SeedAsync();
        await ShouldBeRejectedAsync(
            Invoice(new CreateRecurringLineDto { AccountId = _revenueId, Description = new string('d', 501), Quantity = 1, UnitPrice = 10m }),
            "description");
    }

    [Fact]
    public async Task Create_OverlongName_Rejected400()
    {
        await SeedAsync();
        var input = Invoice();
        input.Name = new string('n', 201);
        await ShouldBeRejectedAsync(input, "name");
    }

    [Fact]
    public async Task Update_ExpenseLineWithoutAnAccount_Rejected400_AndTheTemplateIsUnchanged()
    {
        await SeedAsync();
        var created = await CreateAsync(Expense(new CreateRecurringLineDto { AccountId = _expenseAccountId, Quantity = 1, UnitPrice = 25m }));
        created.Succeeded.ShouldBeTrue(created.Message);

        var updated = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.UpdateAsync(created.Data!.Id, ToUpdate(
            created.Data, [new CreateRecurringLineDto { Quantity = 1, UnitPrice = 30m }])));

        updated.Succeeded.ShouldBeFalse();
        updated.Code.ShouldBe(400);
        var reloaded = await GetAsync(created.Data!.Id);
        reloaded.Lines.ShouldHaveSingleItem().UnitPrice.ShouldBe(25m);
    }

    // ── 原子性 ────────────────────────────────────────────

    [Fact]
    public async Task Update_WhenTheNewLinesFailToSave_KeepsTheOldLinesAndHeader()
    {
        await SeedAsync();
        var created = await CreateAsync(Invoice());
        created.Succeeded.ShouldBeTrue(created.Message);

        var input = ToUpdate(created.Data!, [new CreateRecurringLineDto { AccountId = _revenueId, Quantity = 2, UnitPrice = 750m }]);
        input.Name = "Renamed retainer";
        _failLineInserts.Armed = true;

        await Should.ThrowAsync<Exception>(() =>
            InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.UpdateAsync(created.Data!.Id, input)));

        _failLineInserts.Armed = false;
        var reloaded = await GetAsync(created.Data!.Id);
        reloaded.Name.ShouldBe("Monthly retainer");
        reloaded.Lines.ShouldHaveSingleItem().UnitPrice.ShouldBe(500m);
    }

    [Fact]
    public async Task Create_WhenTheLinesFailToSave_LeavesNoTemplateBehind()
    {
        await SeedAsync();
        _failLineInserts.Armed = true;

        await Should.ThrowAsync<Exception>(() => CreateAsync(Invoice()));

        _failLineInserts.Armed = false;
        (await TemplateCountAsync()).ShouldBe(0);
    }

    // ── 删除 ──────────────────────────────────────────────

    /// <summary>
    /// 从第一期就配错、一张单据都没生成过的模板可以删：失败行随本期事务回滚，没有单据要追溯来历。
    /// </summary>
    [Theory]
    [InlineData(RecurringRunStatus.Failed)]
    [InlineData(RecurringRunStatus.Skipped)]
    public async Task Delete_ATemplateWhoseOnlyRunsProducedNoDocument_Succeeds(RecurringRunStatus status)
    {
        await SeedAsync();
        var created = await CreateAsync(Invoice());
        created.Succeeded.ShouldBeTrue(created.Message);
        await InScopeAsync<IRepository<RecurringRun, Guid>, bool>(async repo =>
        {
            await repo.InsertAsync(new RecurringRun
            {
                RecurringDocumentId = created.Data!.Id,
                PeriodDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = status,
                FailReason = "Counter account was disabled.",
            });
            await repo.SaveChangesAsync();
            return true;
        });

        var deleted = await InScopeAsync<IRecurringDocumentService, Result>(s => s.DeleteAsync(created.Data!.Id));

        deleted.Succeeded.ShouldBeTrue(deleted.Message);
        (await ReloadAsync<RecurringDocument>(created.Data!.Id)).ShouldBeNull();
    }

    private async Task<RecurringDocumentDto> GetAsync(Guid id)
    {
        var result = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.GetAsync(id));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!;
    }

    private static UpdateRecurringDocumentDto ToUpdate(RecurringDocumentDto dto, List<CreateRecurringLineDto> lines) => new()
    {
        Name = dto.Name,
        PartyId = dto.PartyId,
        PaidFromAccountId = dto.PaidFromAccountId,
        Currency = dto.Currency,
        PaymentMethod = dto.PaymentMethod,
        Memo = dto.Memo,
        Frequency = dto.Frequency,
        Interval = dto.Interval,
        AnchorDay = dto.AnchorDay,
        StartDate = dto.StartDate,
        EndDate = dto.EndDate,
        MaxOccurrences = dto.MaxOccurrences,
        DueDays = dto.DueDays,
        AutoPost = dto.AutoPost,
        ConcurrencyStamp = dto.ConcurrencyStamp,
        Lines = lines,
    };

    /// <summary>有新模板行要插入的那一次提交直接失败（相当于数据库拒绝了它）。</summary>
    private sealed class FailLineInsertsInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<RecurringLine>().Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("Injected failure: the template lines could not be saved.");

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
