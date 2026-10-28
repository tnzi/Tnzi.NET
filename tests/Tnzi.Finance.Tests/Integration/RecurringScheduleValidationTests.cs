namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 排期参数的边界：间隔要有上界，枚举要是定义过的值。
/// </summary>
/// <remarks>
/// 被保护的缺陷：保存端只查 <c>Interval &lt; 1</c>，而新建只调 <c>First()</c>（不用间隔），于是
/// 「每 10000 年一次」能存进库，到期那一刻排期越过 9999 年抛异常；更大的值让乘法溢出成负数，
/// 排期倒着走。未定义的周期枚举则静默按「每月」推进。
/// </remarks>
public class RecurringScheduleValidationTests : FinanceIntegrationTestBase
{
    private async Task<CreateRecurringDocumentDto> TemplateInputAsync()
    {
        await SeedCoaAsync();
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = "Bounds Co", Currency = "USD" }));
        customer.Succeeded.ShouldBeTrue(customer.Message);
        var revenue = await AccountIdByCodeAsync("4100");
        return new CreateRecurringDocumentDto
        {
            Name = "Bounded",
            Kind = RecurringDocKind.Invoice,
            PartyId = customer.Data!.Id,
            Currency = "USD",
            Frequency = RecurrenceFrequency.Yearly,
            Interval = 1,
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Lines = [new CreateRecurringLineDto { AccountId = revenue, Quantity = 1, UnitPrice = 100m }],
        };
    }

    private Task<Result<RecurringDocumentDto>> CreateAsync(CreateRecurringDocumentDto input)
        => InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.CreateAsync(input));

    [Theory]
    [InlineData(10000)]
    [InlineData(int.MaxValue)]
    public async Task Create_IntervalBeyondTheUpperBound_IsRejected(int interval)
    {
        var input = await TemplateInputAsync();
        input.Interval = interval;

        var result = await CreateAsync(input);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Create_IntervalAtTheUpperBound_IsAccepted()
    {
        var input = await TemplateInputAsync();
        input.Interval = 1000;

        var result = await CreateAsync(input);

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task Create_UndefinedFrequency_IsRejected()
    {
        var input = await TemplateInputAsync();
        input.Frequency = (RecurrenceFrequency)99;

        var result = await CreateAsync(input);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Create_UndefinedKind_IsRejected()
    {
        var input = await TemplateInputAsync();
        input.Kind = (RecurringDocKind)99;

        var result = await CreateAsync(input);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Preview_IntervalBeyondTheUpperBound_IsRejectedInsteadOfThrowing()
    {
        var input = await TemplateInputAsync();
        input.Interval = 10000;

        var preview = await InScopeAsync<IRecurringDocumentService, Result<RecurrencePreviewDto>>(
            s => Task.FromResult(s.PreviewSchedule(input, 6)));

        preview.Succeeded.ShouldBeFalse();
        preview.Code.ShouldBe(400);
    }
}
