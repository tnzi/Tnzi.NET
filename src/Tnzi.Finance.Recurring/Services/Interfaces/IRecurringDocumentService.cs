namespace Tnzi.Finance.Recurring.Services.Interfaces;

/// <summary>
/// 周期性单据模板管理
/// </summary>
public interface IRecurringDocumentService
{
    Task<Result<IPagedList<RecurringDocumentDto>>> GetPagedAsync(RecurringDocumentQueryDto query, CancellationToken cancellationToken = default);

    Task<Result<RecurringDocumentDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<RecurringDocumentDto>> CreateAsync(CreateRecurringDocumentDto input, CancellationToken cancellationToken = default);

    Task<Result<RecurringDocumentDto>> UpdateAsync(Guid id, UpdateRecurringDocumentDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除模板。
    /// </summary>
    /// <remarks>
    /// 已经生成过单据的模板**不可删**（409）—— 那些单据的来历会因此无从查起；
    /// 该走的路是结束（<see cref="EndAsync"/>）。
    /// </remarks>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>暂停：保留排期但不生成</summary>
    Task<Result<RecurringDocumentDto>> PauseAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复。
    /// </summary>
    /// <remarks>
    /// 恢复时**丢掉已经过去的那些期次**而不是从暂停那天续上：暂停期间的期次是被
    /// 人为决定不要的，续上等于恢复的瞬间凭空补出一批单据。真要补，用手工触发。
    ///
    /// 边界取"今天（含）"：恰好落在恢复当天的那一期照常生成 —— 它是"现在"，不是补齐。
    /// </remarks>
    Task<Result<RecurringDocumentDto>> ResumeAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>结束：此后不再生成，历史保留</summary>
    Task<Result<RecurringDocumentDto>> EndAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>排期预览：接下来 <paramref name="count"/> 期分别落在哪天（不写任何东西）</summary>
    Task<Result<RecurrencePreviewDto>> PreviewAsync(Guid id, int count = 6, CancellationToken cancellationToken = default);

    /// <summary>按排期参数直接预览（模板尚未保存时用）</summary>
    Result<RecurrencePreviewDto> PreviewSchedule(CreateRecurringDocumentDto input, int count = 6);

    Task<Result<IPagedList<RecurringRunDto>>> GetRunsAsync(RecurringRunQueryDto query, CancellationToken cancellationToken = default);
}
