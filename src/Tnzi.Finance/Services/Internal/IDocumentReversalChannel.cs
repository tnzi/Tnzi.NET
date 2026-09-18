namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 「单据代表自己发起」的冲销通道 —— <c>JournalEntryService</c> 的非 HTTP 可达入口。
/// </summary>
/// <remarks>
/// 总账冲销端点对单据投影的凭证一律拒绝（见 <c>JournalEntryService.DocumentProjectedSourceTypes</c>），
/// 而单据自己的作废流程恰恰要冲销同一张凭证。两条路径必须在<b>同一段代码</b>里分开：
/// 用一个 DTO 字段区分会让 HTTP 调用方自填绕过，用两份实现会让期间封账 / 并发 / 事件语义漂移。
/// 所以是 internal 接口 + 显式实现：控制器与 <c>IJournalEntryService</c> 看不见它，
/// 只有 <c>LedgerPostingService.ReverseOnBehalfOfDocumentAsync</c> 走这里。
/// </remarks>
internal interface IDocumentReversalChannel
{
    /// <summary>
    /// 代表 <paramref name="sourceType"/> 单据冲销它自己投影出来的凭证。
    /// 凭证的 <c>SourceType</c> 与 <paramref name="sourceType"/> 不相等时拒绝（409）。
    /// </summary>
    Task<Result<JournalEntryDto>> ReverseOnBehalfOfDocumentAsync(Guid id, string sourceType, ReverseJournalEntryDto input, CancellationToken cancellationToken);
}
