namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// 银行账户档案服务（1:1 挂在资金科目上；流水/支票/EFT 共用）
/// </summary>
public interface IBankAccountService
{
    /// <summary>
    /// 读取本面的部署能力（当前仅“能否存储账号明文”，取决于加密密钥是否已配置）
    /// </summary>
    Task<Result<BankAccountCapabilitiesDto>> GetCapabilitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>分页查询银行账户档案</summary>
    Task<Result<IPagedList<BankAccountDto>>> GetPagedAsync(BankAccountQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取银行账户档案</summary>
    Task<Result<BankAccountDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建银行账户档案（科目须为可过账资金叶子且未被其它档案占用；
    /// 账号明文单向入库需已配置加密密钥）
    /// </summary>
    Task<Result<BankAccountDto>> CreateAsync(CreateBankAccountDto input, CancellationToken cancellationToken = default);

    /// <summary>更新银行账户档案（挂载科目不可变；账号留空保持不变）</summary>
    Task<Result<BankAccountDto>> UpdateAsync(Guid id, UpdateBankAccountDto input, CancellationToken cancellationToken = default);

    /// <summary>删除银行账户档案（被支票/EFT 批次引用时拒绝 409）</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>设置下一张支票号（跳号=换票本；不承诺无缺口）</summary>
    Task<Result<BankAccountDto>> SetNextCheckNumberAsync(Guid id, SetNextCheckNumberDto input, CancellationToken cancellationToken = default);
}
