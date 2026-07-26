namespace Tnzi.Finance.Banking.Services.Interfaces;

/// <summary>
/// 银行流水导入与匹配服务
/// </summary>
/// <remarks>
/// 导入（OFX/CSV 文件或 feed 拉取）→ 去重落 <see cref="BankTransaction"/> → 匹配引擎建议 →
/// 确认生成当前 Draft 对账的 <see cref="ReconciliationLine"/>（ReconciliationService 零改动）。
/// 首版匹配/确认限本位币科目（外币可导入但 suggest/confirm 拒绝）。
/// </remarks>
public interface IBankFeedService
{
    /// <summary>分页查询银行流水</summary>
    Task<Result<IPagedList<BankTransactionDto>>> GetPagedAsync(BankTransactionQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 导入对账单文件（OFX / CSV）。CSV 须提供列映射；逐行去重，唯一索引兜底并发。
    /// </summary>
    Task<Result<BankImportResultDto>> ImportStatementAsync(Guid accountId, BankTransactionSource source, string? fileName, string content, CsvMappingDto? mapping, CancellationToken cancellationToken = default);

    /// <summary>从银行 feed 提供者拉取（按账户档案的 FeedProviderKey/游标增量续拉）</summary>
    Task<Result<BankImportResultDto>> PullFromProviderAsync(PullBankFeedDto input, CancellationToken cancellationToken = default);

    /// <summary>对该科目全部待匹配流水跑匹配引擎（写建议；开启 auto-confirm 且有 Draft 对账时精确匹配直接确认）</summary>
    Task<Result<BankSuggestResultDto>> SuggestMatchesAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>列出某流水的匹配候选（多候选时供用户挑选）</summary>
    Task<Result<List<BankMatchCandidateDto>>> GetCandidatesAsync(Guid bankTransactionId, CancellationToken cancellationToken = default);

    /// <summary>确认匹配（journalLineId 留空则采用建议）：在当前 Draft 对账生成勾选行</summary>
    Task<Result<BankTransactionDto>> ConfirmMatchAsync(Guid bankTransactionId, ConfirmBankMatchDto input, CancellationToken cancellationToken = default);

    /// <summary>撤销匹配（对账已完成则 409；Draft 删勾选行并回 Pending）</summary>
    Task<Result<BankTransactionDto>> UnmatchAsync(Guid bankTransactionId, CancellationToken cancellationToken = default);

    /// <summary>排除流水（噪音行，不入账）</summary>
    Task<Result<BankTransactionDto>> ExcludeAsync(Guid bankTransactionId, CancellationToken cancellationToken = default);

    /// <summary>恢复被排除的流水</summary>
    Task<Result<BankTransactionDto>> RestoreAsync(Guid bankTransactionId, CancellationToken cancellationToken = default);

    /// <summary>由流水创建单据草稿（Expense/PaymentEntry/Transfer，按符号预填）并回链</summary>
    Task<Result<BankDocumentResultDto>> CreateDocumentAsync(Guid bankTransactionId, CreateBankDocumentDto input, CancellationToken cancellationToken = default);

    /// <summary>分页查询导入批次（含批内已匹配行数）</summary>
    Task<Result<IPagedList<BankImportBatchDto>>> GetBatchesAsync(BankImportBatchQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>撤销导入批次（软删批次及其行；批内有已匹配行时 409）</summary>
    Task<Result> DeleteBatchAsync(Guid batchId, CancellationToken cancellationToken = default);
}
