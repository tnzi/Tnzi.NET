namespace Tnzi.Finance.Dtos;

/// <summary>
/// 账本封账状态（响应）
/// </summary>
public class LedgerLockDto
{
    /// <summary>封账日（含当日）。null = 未封账。</summary>
    public DateTime? ClosingDate { get; set; }

    /// <summary>
    /// 是否设了口令。
    /// </summary>
    /// <remarks>只回布尔，哈希与明文都不出现在 DTO 上——呈现端只需要知道"改这个要不要口令"。</remarks>
    public bool IsPasswordProtected { get; set; }

    /// <summary>最近一次变更的说明。</summary>
    public string? Note { get; set; }

    /// <summary>最近一次变更时间（未变更过为 null）。</summary>
    public DateTime? LastChangedTime { get; set; }

    /// <summary>最近一次变更人。</summary>
    public Guid? LastChangedBy { get; set; }
}

/// <summary>
/// 设定 / 推进封账日（请求）
/// </summary>
public class SetLedgerLockDto
{
    /// <summary>新的封账日。null = 解除封账。</summary>
    public DateTime? ClosingDate { get; set; }

    /// <summary>
    /// 当前口令。已设口令时必填且必须匹配，否则 403。
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// 新口令。
    /// </summary>
    /// <remarks>
    /// 语义与其它"可选修改"字段一致：<c>null</c> = 不改动；空串 = 清除口令；有值 = 设为该值。
    /// </remarks>
    public string? NewPassword { get; set; }

    /// <summary>变更说明（"已报 Q2 GST/HST"），进审计留痕。</summary>
    public string? Note { get; set; }
}
