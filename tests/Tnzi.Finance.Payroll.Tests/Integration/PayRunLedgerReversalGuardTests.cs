using Microsoft.Extensions.Configuration;
using Tnzi.Modules;

namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 发薪批次的凭证只能经批次自己的作废端点撤销，不得从总账冲销端点绕过去。
/// </summary>
/// <remarks>
/// <para>
/// 被保护的缺陷：核心 <c>JournalEntryService</c> 那份「单据投影的凭证必须走单据自己的作废端点」清单
/// 硬编码了核心七种来源令牌，Payroll 的 <c>PayRun</c> / <c>PayRun.Payment</c> 从未加进去。
/// 持 <c>finance.journal.update</c> 的操作员对一张 PayRun 凭证调
/// <c>POST admin/finance/journal-entries/{id}/reverse</c> 会成功：总账被抵消了，
/// 而 <c>PayRun</c> 仍是 Posted/Paid、工资单仍算在应付工资子账里。各凭证自身平衡、试算平衡恒为 0，
/// 只有人工把应付工资控制科目与工资单合计对一遍才看得出来。
/// </para>
/// <para>
/// 而且不能简单往清单里补两行：<c>PayRunService.VoidAsync</c> 走的正是被那道门拦着的
/// <c>ILedgerPostingService.ReverseAsync</c>，补进去就把 Payroll 自己的合法作废一并堵死。
/// 所以修法是两件事一起做：子模块经 <see cref="IDocumentProjectedSourceTypeProvider"/> 贡献自己的令牌，
/// 并改走「代表单据发起」的冲销入口 <see cref="ILedgerPostingService.ReverseOnBehalfOfDocumentAsync"/>。
/// </para>
/// </remarks>
public class PayRunLedgerReversalGuardTests : PayrollIntegrationTestBase
{
    private static readonly DateTime PeriodStart = new(2026, 6, 1);
    private static readonly DateTime PeriodEnd = new(2026, 6, 30);
    private static readonly DateTime PayDate = new(2026, 7, 5);

    protected override void ConfigureExtraServices(IServiceCollection services)
    {
        // 镜像 PayrollModule 的贡献者注册（测试基类是模块注册图的手工镜像）
        services.AddSingleton<IDocumentProjectedSourceTypeProvider, PayrollDocumentProjectedSourceTypeProvider>();
    }

    private async Task<(Guid RunId, Guid PostingEntryId)> PostedRunAsync()
    {
        await StandardScenarioAsync();
        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId))).Succeeded.ShouldBeTrue();
        var posted = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(runId));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var payslips = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(runId));
        var slip = await ReloadAsync<Payslip>(payslips.Data!.Single().Id);
        slip!.JournalEntryId.ShouldNotBeNull();
        return (runId, slip.JournalEntryId.Value);
    }

    [Fact]
    public async Task GlReverse_OnAPayRunEntry_IsRejected()
    {
        var (_, entryId) = await PostedRunAsync();

        var reversed = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(entryId, new ReverseJournalEntryDto()));

        reversed.Succeeded.ShouldBeFalse("PayRun 凭证从总账直接冲销会让批次状态与总账分叉");
        reversed.Code.ShouldBe(409);
        reversed.Message!.ShouldContain(PayrollPostingHelper.PayRunSourceType);

        var entry = await ReloadAsync<JournalEntry>(entryId);
        entry!.Status.ShouldBe(JournalEntryStatus.Posted);
        entry.ReversedByEntryId.ShouldBeNull();
    }

    [Fact]
    public async Task GlReverse_OnAPayRunPaymentEntry_IsRejected()
    {
        var (runId, _) = await PostedRunAsync();
        var bankId = await AccountIdByCodeAsync("1120");
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PayAsync(runId, new PayRunPaymentDto
        {
            PaymentAccountId = bankId,
            PaymentDate = PayDate
        }))).Succeeded.ShouldBeTrue();

        var payslips = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(runId));
        var slip = await ReloadAsync<Payslip>(payslips.Data!.Single().Id);
        slip!.PaymentJournalEntryId.ShouldNotBeNull();

        var reversed = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(slip.PaymentJournalEntryId.Value, new ReverseJournalEntryDto()));

        reversed.Succeeded.ShouldBeFalse("付款凭证同样只能经批次作废撤销");
        reversed.Code.ShouldBe(409);
        reversed.Message!.ShouldContain(PayrollPostingHelper.PayRunPaymentSourceType);
    }

    /// <summary>
    /// 回归：清单里有了 PayRun 之后，批次自己的作废仍然能冲销它的两张凭证。
    /// </summary>
    [Fact]
    public async Task PayRunVoid_StillReversesItsOwnEntries()
    {
        var (runId, postingEntryId) = await PostedRunAsync();
        var bankId = await AccountIdByCodeAsync("1120");
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PayAsync(runId, new PayRunPaymentDto
        {
            PaymentAccountId = bankId,
            PaymentDate = PayDate
        }))).Succeeded.ShouldBeTrue();

        var voided = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.VoidAsync(runId));
        voided.Succeeded.ShouldBeTrue(voided.Message);
        voided.Data!.Status.ShouldBe(PayRunStatus.Voided);

        var posting = await ReloadAsync<JournalEntry>(postingEntryId);
        posting!.Status.ShouldBe(JournalEntryStatus.Reversed);

        var payslips = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(runId));
        var slip = await ReloadAsync<Payslip>(payslips.Data!.Single().Id);
        var payment = await ReloadAsync<JournalEntry>(slip!.PaymentJournalEntryId!.Value);
        payment!.Status.ShouldBe(JournalEntryStatus.Reversed);
    }

    /// <summary>
    /// 代表单据发起的冲销只对同一来源类型放行：一个 PayRun 的作废够不到发票的凭证。
    /// </summary>
    [Fact]
    public async Task ReverseOnBehalfOfDocument_WithAnotherSourceType_IsRejected()
    {
        var (_, entryId) = await PostedRunAsync();

        var reversed = await InScopeAsync<ILedgerPostingService, Result<JournalEntryDto>>(
            s => s.ReverseOnBehalfOfDocumentAsync(entryId, FinanceSourceTypes.Invoice));

        reversed.Succeeded.ShouldBeFalse();
        reversed.Code.ShouldBe(409);

        var entry = await ReloadAsync<JournalEntry>(entryId);
        entry!.Status.ShouldBe(JournalEntryStatus.Posted);
    }

    /// <summary>
    /// DI 接线在集成测试里看不见（基类镜像了注册图），所以直接对模块的注册断言。
    /// </summary>
    [Fact]
    public async Task PayrollModule_ContributesItsSourceTypesToTheReversalGuard()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        await new PayrollModule().ConfigureServicesAsync(new ServiceConfigurationContext(services, configuration));

        var provider = services.Single(d => d.ServiceType == typeof(IDocumentProjectedSourceTypeProvider));
        provider.ImplementationType.ShouldBe(typeof(PayrollDocumentProjectedSourceTypeProvider));

        var contributed = new PayrollDocumentProjectedSourceTypeProvider().SourceTypes;
        contributed.ShouldContain(PayrollPostingHelper.PayRunSourceType);
        contributed.ShouldContain(PayrollPostingHelper.PayRunPaymentSourceType);
    }
}
