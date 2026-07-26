namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>刚从银行流水建出来的草稿单据。</summary>
/// <param name="DocType">来源令牌（<see cref="FinanceSourceTypes"/>）</param>
/// <param name="DocId">草稿单据 Id</param>
public readonly record struct BankDraftResult(string DocType, Guid DocId);

/// <summary>
/// 从一条银行流水建出对应的草稿单据（费用 / 收付款 / 资金划转），并按需过账。
/// </summary>
/// <remarks>
/// 从 <see cref="BankFeedService"/> 拆出：银行流水域的其余部分（导入、去重、匹配、
/// 排除、批次）与「这条流水该变成哪种单据」是两件事，后者是纯粹的单据构造 +
/// 分派，与流水的生命周期无关。<br/>
/// public 因为经 DI 注入 public 服务的构造函数（沿 <c>LedgerPostingEngine</c> 等
/// 协作类先例；MS.DI 只解析 public 构造函数，参数类型必须至少同等可访问）。
/// </remarks>
public class BankDocumentDrafter
{
    private readonly IExpenseService _expenseService;
    private readonly IPaymentEntryService _paymentEntryService;
    private readonly ITransferService _transferService;

    public BankDocumentDrafter(
        IExpenseService expenseService,
        IPaymentEntryService paymentEntryService,
        ITransferService transferService)
    {
        _expenseService = Check.NotNull(expenseService);
        _paymentEntryService = Check.NotNull(paymentEntryService);
        _transferService = Check.NotNull(transferService);
    }

    /// <summary>按流水的方向与金额建一张草稿单据（不过账）。</summary>
    public async Task<Result<BankDraftResult>> CreateDraftAsync(BankTransaction txn, CreateBankDocumentDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(txn);
        Check.NotNull(input);

        var amount = Math.Abs(txn.Amount);
        var isInbound = txn.Amount > 0;

        string docType;
        Guid docId;
        switch (input.DocType)
        {
            case BankFeedDocType.Expense:
            {
                if (input.CounterAccountId == null)
                    return Result<BankDraftResult>.Failure("An expense account is required.", 400);
                var expenseResult = await _expenseService.CreateDraftAsync(new CreateExpenseDto
                {
                    PaidFromAccountId = txn.AccountId,
                    DocDate = txn.TxnDate,
                    Currency = txn.Currency,
                    PaymentMethod = input.PaymentMethod,
                    Memo = txn.Description,
                    Lines = new List<CreateExpenseLineDto>
                    {
                        new() { AccountId = input.CounterAccountId.Value, Amount = amount, Description = txn.Description }
                    }
                }, cancellationToken);
                if (!expenseResult.Succeeded)
                    return Result<BankDraftResult>.Failure(expenseResult.Message!, expenseResult.Code ?? 400);
                docType = FinanceSourceTypes.Expense;
                docId = expenseResult.Data!.Id;
                break;
            }
            case BankFeedDocType.PaymentEntry:
            {
                if (input.PartyId == null)
                    return Result<BankDraftResult>.Failure("A party is required for a payment entry.", 400);
                var paymentResult = await _paymentEntryService.CreateDraftAsync(new CreatePaymentEntryDto
                {
                    Direction = isInbound ? PaymentDirection.Inbound : PaymentDirection.Outbound,
                    PartyType = isInbound ? FinancePartyType.Customer : FinancePartyType.Vendor,
                    PartyId = input.PartyId.Value,
                    DocDate = txn.TxnDate,
                    Currency = txn.Currency,
                    Amount = amount,
                    DepositToAccountId = txn.AccountId,
                    PaymentMethod = input.PaymentMethod,
                    Reference = txn.Reference,
                    Memo = txn.Description
                }, cancellationToken);
                if (!paymentResult.Succeeded)
                    return Result<BankDraftResult>.Failure(paymentResult.Message!, paymentResult.Code ?? 400);
                docType = FinanceSourceTypes.PaymentEntry;
                docId = paymentResult.Data!.Id;
                break;
            }
            case BankFeedDocType.Transfer:
            {
                if (input.CounterAccountId == null)
                    return Result<BankDraftResult>.Failure("The other transfer account is required.", 400);
                var transferResult = await _transferService.CreateDraftAsync(new CreateTransferDto
                {
                    FromAccountId = isInbound ? input.CounterAccountId.Value : txn.AccountId,
                    ToAccountId = isInbound ? txn.AccountId : input.CounterAccountId.Value,
                    TransferDate = txn.TxnDate,
                    Currency = txn.Currency,
                    Amount = amount,
                    Reference = txn.Reference,
                    Memo = txn.Description
                }, cancellationToken);
                if (!transferResult.Succeeded)
                    return Result<BankDraftResult>.Failure(transferResult.Message!, transferResult.Code ?? 400);
                docType = FinanceSourceTypes.Transfer;
                docId = transferResult.Data!.Id;
                break;
            }
            default:
                return Result<BankDraftResult>.Failure("Unsupported document type.", 400);
        }

        return Result<BankDraftResult>.Success(new BankDraftResult(docType, docId));
    }

    /// <summary>把刚建出来的草稿单据过账（按来源令牌分派到对应单据服务）。</summary>
    public async Task<Result> PostDraftAsync(string docType, Guid docId, CancellationToken cancellationToken = default)
    {
        if (docType == FinanceSourceTypes.Expense)
        {
            var r = await _expenseService.PostAsync(docId, cancellationToken);
            return r.Succeeded ? Result.Success() : Result.Failure(r.Message!, r.Code ?? 400);
        }
        if (docType == FinanceSourceTypes.PaymentEntry)
        {
            var r = await _paymentEntryService.PostAsync(docId, cancellationToken);
            return r.Succeeded ? Result.Success() : Result.Failure(r.Message!, r.Code ?? 400);
        }
        if (docType == FinanceSourceTypes.Transfer)
        {
            var r = await _transferService.PostAsync(docId, cancellationToken);
            return r.Succeeded ? Result.Success() : Result.Failure(r.Message!, r.Code ?? 400);
        }
        return Result.Failure("Unsupported document type.", 400);
    }
}
