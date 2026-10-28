using System.Text;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 导入去重只查本次文件的键（分批查询），而不是把账户的全部历史键加载进内存：
/// 结果必须与全量加载逐字相同。
/// </summary>
public class BankStatementDedupTests : FinanceIntegrationTestBase
{
    private static CsvMappingDto Mapping() => new()
    {
        HasHeader = true, Delimiter = ",", DateColumn = 0, DateFormat = "yyyy-MM-dd", AmountColumn = 2, DescriptionColumn = 1
    };

    private static string Csv(int from, int count)
    {
        var sb = new StringBuilder("Date,Description,Amount\n");
        for (var i = from; i < from + count; i++)
            sb.Append($"2026-03-{1 + i % 28:00},Line {i},-{1 + i % 50}.00\n");
        return sb.ToString();
    }

    private Task<Result<BankImportResultDto>> ImportCsvAsync(Guid account, string content)
        => InScopeAsync<IBankFeedService, Result<BankImportResultDto>>(
            s => s.ImportStatementAsync(account, BankTransactionSource.Csv, "test.csv", content, Mapping()));

    private Task<Result<BankImportResultDto>> ImportOfxAsync(Guid account, string content)
        => InScopeAsync<IBankFeedService, Result<BankImportResultDto>>(
            s => s.ImportStatementAsync(account, BankTransactionSource.Ofx, "test.ofx", content, null));

    /// <summary>键数超过一次查询的分批上限：重导整份文件，一行都不该再导进来。</summary>
    [Fact]
    public async Task Reimporting_a_file_larger_than_one_key_chunk_skips_every_row()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");

        var first = await ImportCsvAsync(bank, Csv(0, 1500));
        first.Succeeded.ShouldBeTrue(first.Message);
        first.Data!.ImportedCount.ShouldBe(1500);

        var again = await ImportCsvAsync(bank, Csv(0, 1500));
        again.Succeeded.ShouldBeTrue(again.Message);
        again.Data!.ImportedCount.ShouldBe(0);
        again.Data.SkippedCount.ShouldBe(1500);

        // 与历史重叠的文件：只有新的两行导进来
        var overlapping = await ImportCsvAsync(bank, Csv(0, 1502));
        overlapping.Data!.ImportedCount.ShouldBe(2);
        overlapping.Data.SkippedCount.ShouldBe(1500);
    }

    /// <summary>别的账户上同样的 FITID 不算重复：去重键按账户隔离。</summary>
    [Fact]
    public async Task The_same_external_ids_on_another_account_do_not_count_as_duplicates()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");
        var other = await AccountIdByCodeAsync("1110");

        // 另一个账户上已有同样 FITID 的历史行（直接落库，绕过导入路径）
        var batchId = Guid.NewGuid();
        await InScopeAsync<IRepository<BankTransaction, Guid>, int>(async repo =>
        {
            await repo.InsertManyAsync(
            [
                new BankTransaction { AccountId = other, ImportBatchId = batchId, TxnDate = new DateTime(2026, 3, 5), Amount = 1m, Currency = "USD", ExternalId = "FIT-A", Source = BankTransactionSource.Ofx, Status = BankTransactionStatus.Pending },
                new BankTransaction { AccountId = other, ImportBatchId = batchId, TxnDate = new DateTime(2026, 3, 6), Amount = 2m, Currency = "USD", ExternalId = "FIT-B", Source = BankTransactionSource.Ofx, Status = BankTransactionStatus.Pending }
            ]);
            await repo.SaveChangesAsync();
            return 0;
        });

        const string ofx = "<OFX><CURDEF>USD<BANKTRANLIST>"
            + "<STMTTRN><DTPOSTED>20260305<TRNAMT>-1.00<FITID>FIT-A<NAME>A</STMTTRN>"
            + "<STMTTRN><DTPOSTED>20260306<TRNAMT>-2.00<FITID>FIT-B<NAME>B</STMTTRN>"
            + "</BANKTRANLIST></OFX>";

        var imported = await ImportOfxAsync(bank, ofx);
        imported.Succeeded.ShouldBeTrue(imported.Message);
        imported.Data!.ImportedCount.ShouldBe(2);

        var again = await ImportOfxAsync(bank, ofx);
        again.Data!.ImportedCount.ShouldBe(0);
        again.Data.SkippedCount.ShouldBe(2);
    }
}
