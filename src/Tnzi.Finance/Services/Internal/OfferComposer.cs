namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 报价单 / 采购订单共用的行构建与状态机规则。
/// </summary>
/// <remarks>
/// public 因经 DI 注入（见 Services/Internal/ 的既有约定）。两侧单据的行结构与
/// 生命周期规则逐字相同，差异只在往来方与措辞——把这段共用逻辑抽出来，是让两个
/// 服务不可能各自漂移的唯一办法。
/// </remarks>
public class OfferComposer
{
    private readonly FinanceDocumentHelper _helper;
    private readonly FinanceOptions _options;

    public OfferComposer(FinanceDocumentHelper helper, IOptionsSnapshot<FinanceOptions> options)
    {
        _helper = Check.NotNull(helper);
        _options = Check.NotNull(options).Value;
    }

    /// <summary>构建好的行与合计</summary>
    public sealed record Composition(List<ComposedLine> Lines, decimal SubTotal, decimal TaxTotal, decimal Total);

    /// <summary>单行的定稿值</summary>
    public sealed record ComposedLine(
        int LineNumber, Guid? ItemId, string? Description, Guid? AccountId,
        decimal Quantity, decimal UnitPrice, decimal Amount, Guid? TaxCodeId);

    /// <summary>
    /// 校验行、解析目录项默认描述、算行金额与税额。
    /// </summary>
    /// <param name="lines">待定稿的单据行</param>
    /// <param name="isPurchase">true 走进项税口径（采购订单）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task<Result<Composition>> ComposeAsync(
        IReadOnlyList<CreateOfferLineDto>? lines, bool isPurchase, CancellationToken cancellationToken)
    {
        if (lines == null || lines.Count == 0)
            return Result<Composition>.Failure("At least one line is required.", 400);
        if (lines.Count > _options.MaxLinesPerEntry)
            return Result<Composition>.Failure($"Too many lines (max {_options.MaxLinesPerEntry}).", 400);

        var itemIds = lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId!.Value).Distinct().ToList();
        var itemsResult = await _helper.LoadItemsAsync(itemIds, cancellationToken);
        if (!itemsResult.Succeeded)
            return Result<Composition>.Failure(itemsResult.Message ?? "Invalid items.", itemsResult.Code ?? 400);

        var composed = new List<ComposedLine>(lines.Count);
        var lineNo = 1;
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                return Result<Composition>.Failure($"Line {lineNo}: quantity must be greater than zero.", 400);
            if (line.UnitPrice < 0)
                return Result<Composition>.Failure($"Line {lineNo}: unit price must not be negative.", 400);

            var item = line.ItemId.HasValue ? itemsResult.Data![line.ItemId.Value] : null;
            composed.Add(new ComposedLine(
                lineNo++,
                line.ItemId,
                line.Description ?? item?.Description,
                line.AccountId,
                line.Quantity,
                line.UnitPrice,
                _helper.Round(line.Quantity * line.UnitPrice),
                line.TaxCodeId));
        }

        var taxResult = await _helper.CalculateTaxAsync(
            composed.Select(l => new TaxCalculationLine { Amount = l.Amount, TaxCodeId = l.TaxCodeId }).ToList(),
            cancellationToken,
            isPurchase);
        if (!taxResult.Succeeded)
            return Result<Composition>.Failure(taxResult.Message ?? "Tax calculation failed.", taxResult.Code ?? 400);

        var subTotal = _helper.Round(composed.Sum(l => l.Amount));
        var taxTotal = taxResult.Data!.TaxTotal;
        return Result<Composition>.Success(new Composition(composed, subTotal, taxTotal, subTotal + taxTotal));
    }

    /// <summary>可编辑：已转换 / 已关闭的单据是历史，不再改</summary>
    public static bool CanEdit(FinanceOfferStatus status)
        => status is not (FinanceOfferStatus.Converted or FinanceOfferStatus.Closed);

    /// <summary>可发出：草稿，或被拒绝后重新报价</summary>
    public static bool CanSend(FinanceOfferStatus status)
        => status is FinanceOfferStatus.Draft or FinanceOfferStatus.Declined;

    /// <summary>可接受：已发出</summary>
    public static bool CanAccept(FinanceOfferStatus status)
        => status is FinanceOfferStatus.Sent;

    /// <summary>可拒绝：已发出或已接受（对方反悔）</summary>
    public static bool CanDecline(FinanceOfferStatus status)
        => status is FinanceOfferStatus.Sent or FinanceOfferStatus.Accepted;

    /// <summary>可关闭：任何已经发出过的单据</summary>
    public static bool CanClose(FinanceOfferStatus status)
        => status is FinanceOfferStatus.Sent or FinanceOfferStatus.Accepted or FinanceOfferStatus.Declined;

    /// <summary>
    /// 可转换：已发出或已接受。
    /// </summary>
    /// <remarks>
    /// 刻意允许"未正式接受即转换"——客户在电话里说"就按这个做"是最常见的成交方式，
    /// 强制先点一次 Accept 只会让人把状态点成假的。
    /// </remarks>
    public static bool CanConvert(FinanceOfferStatus status)
        => status is FinanceOfferStatus.Sent or FinanceOfferStatus.Accepted;
}
