namespace Tnzi.Finance.Recurring.Services.Internal;

/// <summary>
/// 周期性模板对 <see cref="IMasterDataUsageProvider"/> 的实现：模板也在引用客户 / 供应商 / 目录项 / 科目，
/// 核心的删除守卫要问得到它们。
/// </summary>
/// <remarks>
/// <para>
/// 没有这一条时，删掉一个只出现在一条月度订阅模板上的客户会成功（核心查发票 / 贷项 / 收款都为空，
/// 唯一的 provider 只回答报价单），下一个到期日起每一期都因「Customer not found or inactive」落一行 Failed，
/// 重试到 <c>MaxFailedRetries</c> 后彻底沉默 —— 模板仍是 Active、列表照常显示下次运行日，
/// 而客户再也收不到账单，唯一痕迹是 <c>Finance_RecurringRun</c> 里几行 Failed。
/// </para>
/// <para>
/// 只看**未结束**的模板：已结束的不会再产出任何东西，它引用的主数据可以走。停用（<c>IsActive=false</c>）
/// 与删除不同 —— 那是这条守卫指路的方向，模板生成时会以同一句错误留痕，操作员看得到。
/// </para>
/// <para>
/// 形状与 <c>Tnzi.Finance.Offers</c> 的 <c>OfferMasterDataUsageProvider</c> 一致：只用存在性查询作答，
/// 缺省方向只增加拒绝。
/// </para>
/// </remarks>
public class RecurringMasterDataUsageProvider : IMasterDataUsageProvider
{
    private readonly IReadOnlyRepository<RecurringDocument, Guid> _templateRepository;
    private readonly IReadOnlyRepository<RecurringLine, Guid> _lineRepository;

    public RecurringMasterDataUsageProvider(
        IReadOnlyRepository<RecurringDocument, Guid> templateRepository,
        IReadOnlyRepository<RecurringLine, Guid> lineRepository)
    {
        _templateRepository = Check.NotNull(templateRepository);
        _lineRepository = Check.NotNull(lineRepository);
    }

    /// <inheritdoc />
    public async Task<MasterDataUsage?> FindUsageAsync(
        FinanceMasterDataKind kind, Guid id, CancellationToken cancellationToken = default)
    {
        switch (kind)
        {
            case FinanceMasterDataKind.Customer:
                return await _templateRepository.AnyAsync(
                        t => t.Status != RecurringStatus.Ended && t.Kind == RecurringDocKind.Invoice && t.PartyId == id,
                        cancellationToken)
                    ? new MasterDataUsage("Cannot delete a customer referenced by a recurring template. End or edit the template first, or deactivate the customer instead.")
                    : null;

            case FinanceMasterDataKind.Vendor:
                return await _templateRepository.AnyAsync(
                        t => t.Status != RecurringStatus.Ended && t.Kind != RecurringDocKind.Invoice && t.PartyId == id,
                        cancellationToken)
                    ? new MasterDataUsage("Cannot delete a vendor referenced by a recurring template. End or edit the template first, or deactivate the vendor instead.")
                    : null;

            case FinanceMasterDataKind.Item:
                return await LineOfLiveTemplateAsync(l => l.ItemId == id, cancellationToken)
                    ? new MasterDataUsage("Cannot delete an item referenced by a recurring template line. End or edit the template first, or deactivate the item instead.")
                    : null;

            case FinanceMasterDataKind.Account:
                var onTemplate = await _templateRepository.AnyAsync(
                    t => t.Status != RecurringStatus.Ended && t.PaidFromAccountId == id, cancellationToken);
                var onLine = onTemplate || await LineOfLiveTemplateAsync(l => l.AccountId == id, cancellationToken);
                return onLine
                    ? new MasterDataUsage("Cannot delete an account a recurring template posts to. End or edit the template first.")
                    : null;

            default:
                // 不认识的种类交给别的实现回答，而不是猜一个「没在用」以外的答案。
                return null;
        }
    }

    /// <summary>
    /// 某条行谓词是否命中一条**未结束**模板的行。
    /// </summary>
    private async Task<bool> LineOfLiveTemplateAsync(
        Expression<Func<RecurringLine, bool>> linePredicate, CancellationToken cancellationToken)
    {
        var liveTemplates = _templateRepository.AsQueryable().Where(t => t.Status != RecurringStatus.Ended).Select(t => t.Id);
        return await _lineRepository.AsQueryable()
            .Where(linePredicate)
            .AnyAsync(l => liveTemplates.Contains(l.RecurringDocumentId), cancellationToken);
    }
}
