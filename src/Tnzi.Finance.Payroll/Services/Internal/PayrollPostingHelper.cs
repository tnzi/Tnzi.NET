namespace Tnzi.Finance.Payroll.Services.Internal;

/// <summary>
/// 一个过账/付款分块（一张凭证请求 + 其覆盖的工资单，用于回填凭证引用）
/// </summary>
public sealed record PayrollPostingChunk(LedgerPostingRequest Request, IReadOnlyList<Payslip> Payslips);

/// <summary>
/// 薪酬过账装配助手：解析 WagesPayable 角色科目、按行快照校验组件科目、
/// 按员工分块组装 <see cref="LedgerPostingRequest"/>（防破 MaxLinesPerEntry）。
/// </summary>
/// <remarks>
/// GL 布局：Dr 各 Earning/EmployerContribution 组件费用科目（按科目聚合）/
/// Cr 各 Deduction/EmployerContribution 组件负债科目（按科目聚合）/
/// Cr WagesPayable 角色科目按员工逐行（PartyType="Employee", PartyId=员工Id）。
/// 付款：Dr WagesPayable 按员工逐行 / Cr 资金科目（聚合）。
/// </remarks>
public sealed class PayrollPostingHelper
{
    private readonly IChartOfAccountsService _chartOfAccounts;
    private readonly IRepository<Account, Guid> _accountRepo;
    private readonly FinanceOptions _financeOptions;

    public PayrollPostingHelper(
        IChartOfAccountsService chartOfAccounts,
        IRepository<Account, Guid> accountRepo,
        IOptionsSnapshot<FinanceOptions> financeOptions)
    {
        _chartOfAccounts = Check.NotNull(chartOfAccounts);
        _accountRepo = Check.NotNull(accountRepo);
        _financeOptions = Check.NotNull(financeOptions).Value;
    }

    /// <summary>
    /// 解析 WagesPayable 角色科目（未配置返回指引）。
    /// </summary>
    public async Task<Result<Account>> ResolveWagesPayableAsync(CancellationToken cancellationToken)
    {
        var account = await _chartOfAccounts.FindByRoleAsync(AccountSystemRole.WagesPayable, cancellationToken);
        if (account == null)
        {
            return Result.Failure<Account>(
                "No account is mapped to the Wages Payable system role. Assign the role to a liability account " +
                "(or seed the default chart of accounts) before posting a pay run.", 400);
        }
        if (account.IsGroup || !account.IsActive)
            return Result.Failure<Account>("The Wages Payable account must be an active postable (non-group) account.", 400);

        return Result.Success(account);
    }

    /// <summary>
    /// 按行快照校验组件科目必备性（Earning/EmployerContribution 须有费用科目、
    /// Deduction/EmployerContribution 须有负债科目）。
    /// </summary>
    public static Result ValidatePostingAccounts(IReadOnlyList<Payslip> payslips)
    {
        foreach (var payslip in payslips)
        {
            foreach (var line in payslip.Lines)
            {
                var needsExpense = line.ComponentType is SalaryComponentType.Earning or SalaryComponentType.EmployerContribution;
                var needsLiability = line.ComponentType is SalaryComponentType.Deduction or SalaryComponentType.EmployerContribution;

                if (needsExpense && !line.ExpenseAccountId.HasValue)
                {
                    return Result.Failure(
                        $"Component '{line.ComponentCode}' has no expense account configured; set one before posting.", 400);
                }
                if (needsLiability && !line.LiabilityAccountId.HasValue)
                {
                    return Result.Failure(
                        $"Component '{line.ComponentCode}' has no liability account configured; set one before posting.", 400);
                }
            }
        }
        return Result.Success();
    }

    /// <summary>
    /// 校验付款资金科目（CashEquivalent 可过账叶子、本位币）。
    /// </summary>
    public async Task<Result<Account>> ValidatePaymentAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await _accountRepo.GetAsync(accountId, cancellationToken);
        if (account == null)
            return Result.Failure<Account>("Payment account not found.", 404);
        if (account.IsGroup || !account.IsActive)
            return Result.Failure<Account>("The payment account must be an active postable (non-group) account.", 400);
        if (account.CashFlowActivity != CashFlowActivity.CashEquivalent)
            return Result.Failure<Account>("The payment account must be a cash/bank account (cash-flow activity Cash Equivalent).", 400);

        var baseCurrency = _financeOptions.BaseCurrency.Trim().ToUpperInvariant();
        if (account.Currency != null && !string.Equals(account.Currency, baseCurrency, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<Account>("The payment account must be denominated in the base currency.", 400);

        return Result.Success(account);
    }

    /// <summary>
    /// 组装过账凭证请求（按员工分块防破 MaxLinesPerEntry）。
    /// </summary>
    public Result<List<PayrollPostingChunk>> BuildPostingChunks(PayRun run, IReadOnlyList<Payslip> payslips, Guid wagesPayableAccountId)
    {
        var distinctAccounts = payslips
            .SelectMany(p => p.Lines)
            .SelectMany(AccountsOf)
            .Distinct()
            .Count();

        var maxLines = _financeOptions.MaxLinesPerEntry;
        if (distinctAccounts >= maxLines)
        {
            return Result.Failure<List<PayrollPostingChunk>>(
                $"The pay run posts {distinctAccounts} distinct accounts, which alone exceeds the {maxLines}-line journal-entry limit.", 400);
        }

        // 每块 = distinct 科目行（≤ distinctAccounts）+ 每员工一行 WagesPayable；
        // 块员工数上限 = maxLines - distinctAccounts 保证块行数 ≤ maxLines
        var chunkSize = Math.Max(1, maxLines - distinctAccounts);
        var chunks = new List<PayrollPostingChunk>();

        foreach (var group in Partition(payslips, chunkSize))
        {
            var expenseAgg = new Dictionary<Guid, decimal>();
            var liabilityAgg = new Dictionary<Guid, decimal>();
            var wagesLines = new List<LedgerPostingLine>();

            foreach (var payslip in group)
            {
                foreach (var line in payslip.Lines)
                {
                    if (line.ComponentType is SalaryComponentType.Earning or SalaryComponentType.EmployerContribution)
                        Accumulate(expenseAgg, line.ExpenseAccountId!.Value, line.Amount);
                    if (line.ComponentType is SalaryComponentType.Deduction or SalaryComponentType.EmployerContribution)
                        Accumulate(liabilityAgg, line.LiabilityAccountId!.Value, line.Amount);
                }

                if (payslip.NetPay != 0m)
                {
                    wagesLines.Add(new LedgerPostingLine
                    {
                        AccountId = wagesPayableAccountId,
                        Credit = payslip.NetPay,
                        PartyType = PayrollPartyType,
                        PartyId = payslip.EmployeeId.ToString()
                    });
                }
            }

            var lines = new List<LedgerPostingLine>();
            foreach (var (accountId, amount) in expenseAgg.Where(a => a.Value != 0m))
                lines.Add(new LedgerPostingLine { AccountId = accountId, Debit = amount });
            foreach (var (accountId, amount) in liabilityAgg.Where(a => a.Value != 0m))
                lines.Add(new LedgerPostingLine { AccountId = accountId, Credit = amount });
            lines.AddRange(wagesLines);

            chunks.Add(new PayrollPostingChunk(new LedgerPostingRequest
            {
                PostingDate = run.PayDate.ToUtcDate(),
                Memo = $"Payroll {run.PeriodStart:yyyy-MM-dd} to {run.PeriodEnd:yyyy-MM-dd}",
                SourceType = PayRunSourceType,
                SourceId = run.Id.ToString(),
                Lines = lines
            }, group));
        }

        return Result.Success(chunks);
    }

    /// <summary>
    /// 组装付款凭证请求（Dr WagesPayable 按员工逐行 / Cr 资金科目聚合；按员工分块）。
    /// 仅 NetPay &gt; 0 的工资单入块（零净额工资单由调用方直接标记已付）。
    /// </summary>
    public List<PayrollPostingChunk> BuildPaymentChunks(
        PayRun run, IReadOnlyList<Payslip> payslips, Guid wagesPayableAccountId, Guid paymentAccountId, DateTime paymentDate)
    {
        var maxLines = _financeOptions.MaxLinesPerEntry;
        // 每块 = 每员工一行 WagesPayable + 一行资金科目
        var chunkSize = Math.Max(1, maxLines - 1);
        var chunks = new List<PayrollPostingChunk>();

        foreach (var group in Partition(payslips, chunkSize))
        {
            var lines = new List<LedgerPostingLine>();
            var total = 0m;
            foreach (var payslip in group)
            {
                lines.Add(new LedgerPostingLine
                {
                    AccountId = wagesPayableAccountId,
                    Debit = payslip.NetPay,
                    PartyType = PayrollPartyType,
                    PartyId = payslip.EmployeeId.ToString()
                });
                total += payslip.NetPay;
            }
            lines.Add(new LedgerPostingLine { AccountId = paymentAccountId, Credit = total });

            chunks.Add(new PayrollPostingChunk(new LedgerPostingRequest
            {
                PostingDate = paymentDate.ToUtcDate(),
                Memo = $"Payroll payment {run.PeriodStart:yyyy-MM-dd} to {run.PeriodEnd:yyyy-MM-dd}",
                SourceType = PayRunPaymentSourceType,
                SourceId = run.Id.ToString(),
                Lines = lines
            }, group));
        }

        return chunks;
    }

    /// <summary>过账来源类型（Finance PostingGuard 的 DocType；消费方审批门拦此）</summary>
    public const string PayRunSourceType = "PayRun";

    /// <summary>付款来源类型</summary>
    public const string PayRunPaymentSourceType = "PayRun.Payment";

    /// <summary>GL 行往来方类型（钻取维度；否决 FinancePartyType.Employee 后的自由字符串）</summary>
    public const string PayrollPartyType = "Employee";

    private static IEnumerable<Guid> AccountsOf(PayslipLine line)
    {
        if (line.ComponentType is SalaryComponentType.Earning or SalaryComponentType.EmployerContribution && line.ExpenseAccountId.HasValue)
            yield return line.ExpenseAccountId.Value;
        if (line.ComponentType is SalaryComponentType.Deduction or SalaryComponentType.EmployerContribution && line.LiabilityAccountId.HasValue)
            yield return line.LiabilityAccountId.Value;
    }

    private static void Accumulate(Dictionary<Guid, decimal> agg, Guid accountId, decimal amount)
        => agg[accountId] = agg.GetValueOrDefault(accountId) + amount;

    private static IEnumerable<IReadOnlyList<Payslip>> Partition(IReadOnlyList<Payslip> payslips, int chunkSize)
    {
        for (var i = 0; i < payslips.Count; i += chunkSize)
            yield return payslips.Skip(i).Take(chunkSize).ToList();
    }
}
