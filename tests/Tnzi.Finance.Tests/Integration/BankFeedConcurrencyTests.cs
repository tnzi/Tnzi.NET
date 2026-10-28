using System.Linq.Expressions;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 银行流水上的「读出来、看状态、整行写回」写路径必须是比对写（<c>BankTransaction.ConcurrencyStamp</c>）。
/// </summary>
/// <remarks>
/// 每条用例在被测操作的「读」与「写」之间插进另一个操作员的一次完整操作（钩子只触发一次）。
/// 没有并发标记时它们全是后写者赢：建议匹配把期间被确认的行整行写回 Pending（勾选行留在草稿对账里、
/// 那条总账行不再是候选，这笔钱只能再建一张单据）；两个「新建并对账」各过一次账；排除把已匹配的行
/// 改成 Excluded；删批次把刚被确认的行一起软删。
/// </remarks>
public class BankFeedConcurrencyTests : FinanceIntegrationTestBase
{
    private readonly FeedHooks _hooks = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        // 后注册者胜：钩子默认不挂动作，与基类注册逐字相同。
        services.AddScoped<BankMatchEngine, HookedBankMatchEngine>();
        services.AddSingleton(_hooks);
        services.AddScoped<IRepository<BankTransaction, Guid>>(sp =>
            new HookedTxnRepository(sp.GetRequiredService<FinanceTestDbContext>(), sp, _hooks));
        services.AddScoped<IReadOnlyRepository<BankTransaction, Guid>>(sp => sp.GetRequiredService<IRepository<BankTransaction, Guid>>());
    }

    private static CsvMappingDto Mapping() => new()
    {
        HasHeader = true, Delimiter = ",", DateColumn = 0, DateFormat = "yyyy-MM-dd", AmountColumn = 2, DescriptionColumn = 1
    };

    private async Task<Guid> ImportAsync(Guid bank, string csv)
    {
        var import = await InScopeAsync<IBankFeedService, Result<BankImportResultDto>>(
            s => s.ImportStatementAsync(bank, BankTransactionSource.Csv, "test.csv", csv, Mapping()));
        import.Succeeded.ShouldBeTrue(import.Message);
        return import.Data!.BatchId;
    }

    private async Task<BankTransaction> SingleTxnAsync(Guid bank)
    {
        var repo = ServiceProvider.GetRequiredService<IRepository<BankTransaction, Guid>>();
        return (await repo.ToListAsync(t => t.AccountId == bank)).Single();
    }

    private async Task<Guid> PostBankLineAsync(Guid bank, decimal amount, DateTime date)
    {
        var posted = await PostLedgerAsync(new LedgerPostingRequest
        {
            PostingDate = date,
            SourceType = "Test.Bank",
            SourceId = Guid.NewGuid().ToString("N"),
            Lines =
            [
                new LedgerPostingLine { AccountId = bank, Debit = amount },
                new LedgerPostingLine { AccountCode = "3100", Credit = amount }
            ]
        });
        posted.Succeeded.ShouldBeTrue(posted.Message);
        var repo = ServiceProvider.GetRequiredService<IRepository<JournalLine, Guid>>();
        return (await repo.FirstOrDefaultAsync(l => l.JournalEntryId == posted.Data!.Id && l.AccountId == bank))!.Id;
    }

    private async Task CreateDraftReconAsync(Guid bank, decimal ending)
    {
        var recon = await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(s => s.CreateDraftAsync(
            new CreateReconciliationDto { AccountId = bank, StatementDate = new DateTime(2026, 3, 31), StatementEndingBalance = ending }));
        recon.Succeeded.ShouldBeTrue(recon.Message);
    }

    private Task<Result<BankTransactionDto>> ConfirmAsync(Guid txn, Guid line)
        => InScopeAsync<IBankFeedService, Result<BankTransactionDto>>(
            s => s.ConfirmMatchAsync(txn, new ConfirmBankMatchDto { JournalLineId = line }));

    private async Task ShouldStillBeMatchedAsync(Guid txnId, Guid line)
    {
        var txn = (await ReloadAsync<BankTransaction>(txnId))!;
        txn.Status.ShouldBe(BankTransactionStatus.Matched);
        txn.MatchedJournalLineId.ShouldBe(line);
        txn.ReconciliationLineId.ShouldNotBeNull();
        (await ReloadAsync<ReconciliationLine>(txn.ReconciliationLineId!.Value)).ShouldNotBeNull();
    }

    /// <summary>
    /// ★★ 建议匹配读出 Pending 之后那一行被确认：建议必须 409，不得把确认整行写回 Pending。
    /// </summary>
    [Fact]
    public async Task Suggest_WhenARowIsConfirmedMidway_Returns409_AndKeepsTheMatch()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");
        var line = await PostBankLineAsync(bank, 500m, new DateTime(2026, 3, 5));
        await ImportAsync(bank, "Date,Description,Amount\n2026-03-05,Deposit,500.00\n");
        await CreateDraftReconAsync(bank, 500m);
        var txn = await SingleTxnAsync(bank);
        Result<BankTransactionDto>? confirmed = null;
        _hooks.AfterCandidates = async () => confirmed = await ConfirmAsync(txn.Id, line);

        var suggest = await InScopeAsync<IBankFeedService, Result<BankSuggestResultDto>>(s => s.SuggestMatchesAsync(bank));

        confirmed.ShouldNotBeNull("钩子必须真的插进了建议匹配");
        confirmed.Succeeded.ShouldBeTrue(confirmed.Message);
        suggest.Succeeded.ShouldBeFalse("这一行的建议变了，整行写回会抹掉别人的确认");
        suggest.Code.ShouldBe(409);
        await ShouldStillBeMatchedAsync(txn.Id, line);
    }

    /// <summary>
    /// 建议没变的行不写：期间被确认的那一行若建议原样不动，建议照常成功、确认原样保留。
    /// </summary>
    [Fact]
    public async Task Suggest_LeavesRowsWhoseSuggestionDidNotChangeUntouched()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");
        var line = await PostBankLineAsync(bank, 500m, new DateTime(2026, 3, 5));
        await ImportAsync(bank, "Date,Description,Amount\n2026-03-05,Deposit,500.00\n");
        (await InScopeAsync<IBankFeedService, Result<BankSuggestResultDto>>(s => s.SuggestMatchesAsync(bank))).Succeeded.ShouldBeTrue();
        await CreateDraftReconAsync(bank, 500m);
        var txn = await SingleTxnAsync(bank);
        txn.SuggestedJournalLineId.ShouldBe(line);
        _hooks.AfterCandidates = async () => (await ConfirmAsync(txn.Id, line)).Succeeded.ShouldBeTrue();

        var suggest = await InScopeAsync<IBankFeedService, Result<BankSuggestResultDto>>(s => s.SuggestMatchesAsync(bank));

        _hooks.AfterCandidates.ShouldBeNull("钩子必须真的插进了建议匹配");
        suggest.Succeeded.ShouldBeTrue(suggest.Message);
        await ShouldStillBeMatchedAsync(txn.Id, line);
    }

    /// <summary>
    /// ★★ 两个「新建并对账」读到同一行 Pending（双击 / 重试 / 与自动入账交错）：只能记一笔钱。
    /// </summary>
    [Fact]
    public async Task CreateDocument_DoubleSubmit_SecondIsRejected_BeforeCreatingAnything()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");
        var expenseAccount = await AccountIdByCodeAsync("5200");
        await ImportAsync(bank, "Date,Description,Amount\n2026-03-10,Office supplies,-120.00\n");
        await CreateDraftReconAsync(bank, -120m);
        var txn = await SingleTxnAsync(bank);
        var input = new CreateBankDocumentDto
        {
            DocType = BankFeedDocType.Expense, CounterAccountId = expenseAccount, PaymentMethod = "Check", PostAndMatch = true
        };
        Result<BankDocumentResultDto>? first = null;
        _hooks.BeforeFirstUpdate = async () =>
            first = await InScopeAsync<IBankFeedService, Result<BankDocumentResultDto>>(s => s.CreateDocumentAsync(txn.Id, input));

        var second = await InScopeAsync<IBankFeedService, Result<BankDocumentResultDto>>(s => s.CreateDocumentAsync(txn.Id, input));

        first.ShouldNotBeNull("钩子必须真的插进了建单");
        first.Succeeded.ShouldBeTrue(first.Message);
        first.Data!.Matched.ShouldBeTrue();
        second.Succeeded.ShouldBeFalse("同一行流水已经建单过账，第二次必须在建单之前被拒");
        second.Code.ShouldBe(409);
        var expenses = ServiceProvider.GetRequiredService<IRepository<Expense, Guid>>();
        (await expenses.CountAsync()).ShouldBe(1);
        var reloaded = (await ReloadAsync<BankTransaction>(txn.Id))!;
        reloaded.Status.ShouldBe(BankTransactionStatus.Matched);
        reloaded.CreatedDocId.ShouldBe(first.Data.DocId);
    }

    /// <summary>
    /// 排除读出 Pending 之后那一行被确认：排除必须 409，已匹配的行不得被改成 Excluded。
    /// </summary>
    [Fact]
    public async Task Exclude_WhenTheRowIsConfirmedMidway_Returns409_AndKeepsTheMatch()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");
        var line = await PostBankLineAsync(bank, 500m, new DateTime(2026, 3, 5));
        await ImportAsync(bank, "Date,Description,Amount\n2026-03-05,Deposit,500.00\n");
        await CreateDraftReconAsync(bank, 500m);
        var txn = await SingleTxnAsync(bank);
        _hooks.BeforeFirstUpdate = async () => (await ConfirmAsync(txn.Id, line)).Succeeded.ShouldBeTrue();

        var excluded = await InScopeAsync<IBankFeedService, Result<BankTransactionDto>>(s => s.ExcludeAsync(txn.Id));

        _hooks.BeforeFirstUpdate.ShouldBeNull("钩子必须真的插进了排除");
        excluded.Succeeded.ShouldBeFalse();
        excluded.Code.ShouldBe(409);
        await ShouldStillBeMatchedAsync(txn.Id, line);
    }

    /// <summary>
    /// 删批次的「有没有已匹配行」检查之后有一行被确认：删除必须 409，那一行与它的勾选行都留着。
    /// </summary>
    [Fact]
    public async Task DeleteBatch_WhenARowIsConfirmedAfterTheCheck_Returns409_AndKeepsEverything()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");
        var line = await PostBankLineAsync(bank, 500m, new DateTime(2026, 3, 5));
        var batchId = await ImportAsync(bank, "Date,Description,Amount\n2026-03-05,Deposit,500.00\n2026-03-06,Fee,-3.00\n");
        await CreateDraftReconAsync(bank, 500m);
        var repo = ServiceProvider.GetRequiredService<IRepository<BankTransaction, Guid>>();
        var deposit = (await repo.ToListAsync(t => t.AccountId == bank && t.Amount == 500m)).Single();
        _hooks.AfterFirstAny = async () => (await ConfirmAsync(deposit.Id, line)).Succeeded.ShouldBeTrue();

        var deleted = await InScopeAsync<IBankFeedService, Result>(s => s.DeleteBatchAsync(batchId));

        _hooks.AfterFirstAny.ShouldBeNull("钩子必须真的插进了删批次");
        deleted.Succeeded.ShouldBeFalse("刚被确认的行不能跟着批次一起删掉");
        deleted.Code.ShouldBe(409);
        await ShouldStillBeMatchedAsync(deposit.Id, line);
        (await repo.CountAsync(t => t.ImportBatchId == batchId)).ShouldBe(2, "整体回滚：未匹配的那一行也还在");
        (await ReloadAsync<BankImportBatch>(batchId)).ShouldNotBeNull();
    }

    /// <summary>对照：没有并发时，删批次照常软删全部行（含超过批量阈值的大批次）。</summary>
    [Fact]
    public async Task DeleteBatch_WithoutConcurrency_SoftDeletesEveryRow()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");
        var csv = "Date,Description,Amount\n" + string.Concat(Enumerable.Range(1, 25).Select(i => $"2026-03-05,Row {i},-{i}.00\n"));
        var batchId = await ImportAsync(bank, csv);

        var deleted = await InScopeAsync<IBankFeedService, Result>(s => s.DeleteBatchAsync(batchId));

        deleted.Succeeded.ShouldBeTrue(deleted.Message);
        var repo = ServiceProvider.GetRequiredService<IRepository<BankTransaction, Guid>>();
        (await repo.CountAsync(t => t.ImportBatchId == batchId)).ShouldBe(0);
    }

    private sealed class FeedHooks
    {
        public Func<Task>? AfterCandidates { get; set; }
        public Func<Task>? BeforeFirstUpdate { get; set; }
        public Func<Task>? AfterFirstAny { get; set; }
    }

    /// <summary>建议匹配：读出 Pending 行、取回候选之后，插入另一个操作。</summary>
    private sealed class HookedBankMatchEngine(
        IReadOnlyRepository<JournalLine, Guid> journalLineRepository,
        IReadOnlyRepository<ReconciliationLine, Guid> reconLineRepository,
        IReadOnlyRepository<BankTransaction, Guid> bankTxnRepository,
        IOptionsSnapshot<FinanceOptions> options,
        FeedHooks hooks)
        : BankMatchEngine(journalLineRepository, reconLineRepository, bankTxnRepository, options)
    {
        public override async Task<Dictionary<decimal, List<BankMatchCandidate>>> GetCandidatesByAmountAsync(
            Guid accountId, IReadOnlyCollection<decimal> amounts, CancellationToken cancellationToken)
        {
            var candidates = await base.GetCandidatesByAmountAsync(accountId, amounts, cancellationToken);
            var hook = hooks.AfterCandidates;
            hooks.AfterCandidates = null;
            if (hook != null)
                await hook();
            return candidates;
        }
    }

    /// <summary>流水仓储：第一次整行写之前 / 第一次存在性检查之后，插入另一个操作。</summary>
    private sealed class HookedTxnRepository(FinanceTestDbContext dbContext, IServiceProvider sp, FeedHooks hooks)
        : EFCoreRepository<FinanceTestDbContext, BankTransaction, Guid>(dbContext, serviceProvider: sp)
    {
        public override async Task UpdateAsync(BankTransaction entity, CancellationToken cancellationToken = default)
        {
            var hook = hooks.BeforeFirstUpdate;
            hooks.BeforeFirstUpdate = null;
            if (hook != null)
                await hook();
            await base.UpdateAsync(entity, cancellationToken);
        }

        public override async Task<bool> AnyAsync(Expression<Func<BankTransaction, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            var result = await base.AnyAsync(predicate, cancellationToken);
            var hook = hooks.AfterFirstAny;
            hooks.AfterFirstAny = null;
            if (hook != null)
                await hook();
            return result;
        }
    }
}
