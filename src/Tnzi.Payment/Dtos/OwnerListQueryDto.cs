namespace Tnzi.Payment.Dtos;

/// <summary>
/// 「我的 X」列表（我的发票 / 我的订阅）的分页参数。
/// </summary>
/// <remarks>
/// 默认每页 100、上限 100：这两个端点此前固定只回最新 100 条且不收任何参数，超过 100 条的部分无从取到。
/// 默认值与那个口径一致，所以不带参数的调用拿到的仍是同一页；要更早的记录就翻页。
/// </remarks>
public class OwnerListQueryDto : PagedQueryDto
{
    /// <inheritdoc />
    protected override int DefaultPageSize => 100;

    /// <inheritdoc />
    protected override int MaxPageSize => 100;
}
