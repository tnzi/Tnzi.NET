namespace Tnzi.Finance.Services;

/// <summary>
/// 会计凭证服务（草稿工作流 + 过账 + 冲销）
/// </summary>
public interface IJournalEntryService
{
    /// <summary>获取凭证（含分录行）</summary>
    Task<Result<JournalEntryDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>分页查询凭证（仅头部）</summary>
    Task<Result<IPagedList<JournalEntryDto>>> GetListAsync(JournalEntryQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>创建凭证草稿</summary>
    Task<Result<JournalEntryDto>> CreateDraftAsync(CreateJournalEntryDto input, CancellationToken cancellationToken = default);

    /// <summary>更新凭证草稿（整体替换头部与分录行；仅草稿可更新）</summary>
    Task<Result<JournalEntryDto>> UpdateDraftAsync(Guid id, CreateJournalEntryDto input, CancellationToken cancellationToken = default);

    /// <summary>删除凭证草稿（仅草稿可删除）</summary>
    Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>过账（校验平衡/科目/期间锁定，分配连续凭证号，落总账）</summary>
    Task<Result<JournalEntryDto>> PostAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>冲销已过账凭证（生成等额反向凭证，原凭证标记 Reversed）</summary>
    Task<Result<JournalEntryDto>> ReverseAsync(Guid id, ReverseJournalEntryDto input, CancellationToken cancellationToken = default);
}
