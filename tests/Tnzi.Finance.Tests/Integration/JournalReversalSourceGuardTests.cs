using Tnzi.Finance.Services.Internal;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 总账冲销端点与单据作废端点的分工，以及冲销漏斗里的「只能冲销一次」。
/// </summary>
/// <remarks>
/// <para>
/// 被保护的缺陷：<c>POST admin/finance/journal-entries/{id}/reverse</c> 只看凭证自身状态、
/// <b>不看 <c>SourceType</c></b>，于是发票/账单/费用/贷项/收付款投影出来的凭证可以从总账直接冲销；
/// 而六个单据 <c>VoidAsync</c> 里有五个取原凭证时<b>不带 <c>Status</c> 谓词</b>
/// （<c>TransferService</c> 带了，证明是遗漏）。两者叠加 = 同一张凭证被冲销两次：
/// 先经总账冲销（原凭证置 <c>Reversed</c>、<c>ReversedByEntryId</c> 指向 R1），再走单据 void ——
/// 单据自身仍是 <c>Posted</c> 故状态门全过，于是再造 R2 并把 <c>ReversedByEntryId</c> 覆写，R1 成孤儿。
/// </para>
/// <para>
/// ★ <b>破坏完全静默</b>：每张凭证内部各自平衡，试算平衡恒为 0；余额汇总忠实累加，
/// <c>VerifyAsync</c> 报「一致」。唯一能暴露它的是人工把 AR/AP 控制科目余额与账龄合计对一遍 ——
/// 而那正是审计首查的关系。
/// </para>
/// </remarks>
public class JournalReversalSourceGuardTests : FinanceIntegrationTestBase
{
    /// <summary>
    /// 单据投影出来的凭证必须经单据自己的作废端点撤销。
    /// </summary>
    /// <remarks>
    /// 从总账冲销一张付款单的凭证，总账被抵消了而付款单自身仍是 <c>Posted</c> ——
    /// 控制科目与子账从此对不上，且两侧各自看起来都正常。
    /// </remarks>
    [Fact]
    public async Task GlReverse_OnADocumentProjectedEntry_IsRejected()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");

        var vendor = await InScopeAsync<IVendorService, Result<VendorDto>>(
            s => s.CreateAsync(new CreateVendorDto { Name = "Reversal Guard Vendor" }));
        vendor.Succeeded.ShouldBeTrue(vendor.Message);

        var draft = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(
            s => s.CreateDraftAsync(new CreatePaymentEntryDto
            {
                Direction = PaymentDirection.Outbound,
                PartyType = FinancePartyType.Vendor,
                PartyId = vendor.Data!.Id,
                DocDate = new DateTime(2026, 5, 6),
                Amount = 150m,
                DepositToAccountId = bank
            }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var posted = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(
            s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var reversed = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(posted.Data!.JournalEntryId!.Value, new ReverseJournalEntryDto()));

        reversed.Succeeded.ShouldBeFalse("单据投影的凭证从总账直接冲销会让单据状态与总账分叉");
        reversed.Code.ShouldBe(409);
        reversed.Message!.ShouldContain(FinanceSourceTypes.PaymentEntry);
    }

    /// <summary>
    /// 期末重估的凭证<b>仍然</b>可以从总账冲销 —— 那是它唯一的撤销路径。
    /// </summary>
    /// <remarks>
    /// 这条防的是把守卫做过头：<c>FinanceSourceTypes.All</c> 里含 <c>Revaluation</c> 与
    /// <c>PaymentApplication</c>，而这两者<b>没有</b>单据级作废端点 ——
    /// <c>RevaluationService</c> 自己的错误消息就写着「Reverse it before revaluing an earlier
    /// or equal date」。一起拦下会堵死一条既定流程且没有替代路径。
    /// </remarks>
    [Fact]
    public async Task GlReverse_OnARevaluationEntry_IsStillAllowed()
    {
        await SeedCoaAsync();

        var request = SimpleSale(80m, new DateTime(2026, 5, 7));
        request.SourceType = FinanceSourceTypes.Revaluation;
        request.SourceId = "2026-05-07";

        var posted = await PostLedgerAsync(request);
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var reversed = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(posted.Data!.Id, new ReverseJournalEntryDto()));

        reversed.Succeeded.ShouldBeTrue(
            $"重估凭证没有单据级作废端点，总账冲销是它唯一的撤销路径：{reversed.Message}");
    }

    /// <summary>
    /// 冲销漏斗自己拒绝已被冲销过的凭证 —— 纵深防御，不依赖任何上游记得检查。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 直接对 <see cref="LedgerPostingEngine"/> 断言而不是走某个端点：上面那条 SourceType 门
    /// 已经把可达的利用路径堵上了，所以这条在公开 API 上<b>够不到</b> —— 但它守的正是
    /// 「将来又多一个上游忘了检查」这件事，而那是本缺陷的原始形态（七个上游里五个没检查）。
    /// </para>
    /// <para>
    /// <c>ReversalGuard</c> 的注释曾断言「凭证状态不在这里判定 —— 上游各 VoidAsync 与
    /// ReverseAsync 已经在做」。<b>注释被当断言用了。</b>
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ReversalFunnel_RejectsAnEntryThatWasAlreadyReversed()
    {
        await SeedCoaAsync();
        var posted = await PostLedgerAsync(SimpleSale(120m, new DateTime(2026, 5, 8), sourceId: "funnel-1"));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var first = await InScopeAsync<ILedgerPostingService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(posted.Data!.Id));
        first.Succeeded.ShouldBeTrue(first.Message);

        // 绕过所有上游，直接问漏斗：这张已经冲销过的凭证还能再冲一次吗？
        var second = await InScopeAsync<LedgerPostingEngine, Result<JournalEntry>>(async engine =>
        {
            var original = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
                s => s.GetAsync(posted.Data!.Id));
            original.Data!.Status.ShouldBe(JournalEntryStatus.Reversed);

            return await engine.BuildReversalAsync(
                new JournalEntry
                {
                    Id = posted.Data!.Id,
                    Status = original.Data.Status,
                    ReversedByEntryId = original.Data.ReversedByEntryId,
                    PostingDate = original.Data.PostingDate,
                    Currency = original.Data.Currency,
                    ExchangeRate = original.Data.ExchangeRate,
                },
                new DateTime(2026, 5, 8),
                "second reversal");
        });

        second.Succeeded.ShouldBeFalse("漏斗必须拒绝已被冲销过的凭证，不能指望每个上游都记得检查");
        second.Code.ShouldBe(409);
    }
}
