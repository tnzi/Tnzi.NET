namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 敏感字段加密的附加认证数据（AAD）派生——把密文绑定到其归属记录，
/// 使加密的账号无法被搬移到另一条记录复用。加密方与解密方必须用**同一**公式，
/// 故集中在此，禁止各服务各写一份。归属键取创建时即已确定且稳定的标识
/// （BankAccount 用 AccountId 唯一键；PartyBankAccount 用往来方类型+Id，
/// 记录自身 Id 在 SaveChanges 前尚未生成，不可用作 AAD）。
/// </summary>
internal static class FinanceProtectionAad
{
    /// <summary>银行账户档案的账号 AAD（AccountId=资金科目 FK，每档案唯一）。</summary>
    public static string ForBankAccount(Guid accountId)
        => $"Finance:BankAccount:{accountId:N}";

    /// <summary>往来方银行账户（remit-to）的账号 AAD（往来方类型+Id）。</summary>
    public static string ForPartyBankAccount(FinancePartyType partyType, Guid partyId)
        => $"Finance:PartyBankAccount:{partyType}:{partyId:N}";
}
