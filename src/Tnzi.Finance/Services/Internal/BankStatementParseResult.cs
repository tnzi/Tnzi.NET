namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 解析出的单条银行流水（归一化中间形态；CSV 的去重键由服务据行内容计算）
/// </summary>
internal sealed record ParsedBankTransaction(
    DateTime PostedDate,
    decimal Amount,
    string? Currency,
    string? ExternalId,
    string? Description,
    string? Payee,
    string? Reference,
    decimal? BalanceAfter = null);

/// <summary>
/// 对账单/流水文件解析结果
/// </summary>
internal sealed class BankStatementParseResult
{
    /// <summary>解析出的流水行</summary>
    public List<ParsedBankTransaction> Transactions { get; init; } = new();

    /// <summary>默认币种（OFX CURDEF）</summary>
    public string? Currency { get; set; }

    /// <summary>对账单声明的账号（OFX BANKACCTFROM/ACCTID）——供导入时与目标账户档案交叉校验，防串号导入</summary>
    public string? StatementAccountId { get; set; }

    /// <summary>对账单期末余额（OFX LEDGERBAL）</summary>
    public decimal? LedgerBalance { get; set; }

    /// <summary>区间起</summary>
    public DateTime? PeriodFrom { get; set; }

    /// <summary>区间止</summary>
    public DateTime? PeriodTo { get; set; }
}
