namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// 往来方银行账户服务（remit-to：客户/供应商结构化账户，EFT 输出的收款方来源）
/// </summary>
public interface IPartyBankAccountService
{
    /// <summary>分页查询往来方银行账户</summary>
    Task<Result<IPagedList<PartyBankAccountDto>>> GetPagedAsync(PartyBankAccountQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取往来方银行账户</summary>
    Task<Result<PartyBankAccountDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>按往来方列出其银行账户（默认账户排在前）</summary>
    Task<Result<List<PartyBankAccountDto>>> GetByPartyAsync(FinancePartyType partyType, Guid partyId, CancellationToken cancellationToken = default);

    /// <summary>创建往来方银行账户（账号明文单向入库需已配置加密密钥）</summary>
    Task<Result<PartyBankAccountDto>> CreateAsync(SavePartyBankAccountDto input, CancellationToken cancellationToken = default);

    /// <summary>更新往来方银行账户（账号留空保持不变；往来方归属不可变）</summary>
    Task<Result<PartyBankAccountDto>> UpdateAsync(Guid id, SavePartyBankAccountDto input, CancellationToken cancellationToken = default);

    /// <summary>删除往来方银行账户</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>设为默认账户（同一事务内清除该往来方的旧默认）</summary>
    Task<Result<PartyBankAccountDto>> SetDefaultAsync(Guid id, CancellationToken cancellationToken = default);
}
