using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 存量分享链接（单轮 HMAC 口令哈希）在第一次验证通过时就地升级成 PBKDF2。
/// </summary>
/// <remarks>
/// 升级只能发生在「手里有明文口令」的那一刻，也就是收件人输对口令的那次请求；
/// 后台批量重算是做不到的（库里只有哈希）。所以这条路径要么在这里，要么永远不会发生。
/// </remarks>
public class ShareLinkPasswordUpgradeTests : WorkspaceIntegrationTestBase
{
    private readonly BeforeUpdateHook _hook = new();

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(_hook);

    private string ShareTable => DbContext.Model.FindEntityType(typeof(FileShare))!.GetTableName()!;

    private static string LegacyHash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        using var hmac = new HMACSHA256(salt);
        return $"{Convert.ToHexString(salt)}:{Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(password)))}".ToLowerInvariant();
    }

    private async Task<FileShare> SeedLegacyShareAsync(string password)
    {
        var file = await CreateStoredFileAsync("legacy.txt", "bytes"u8.ToArray());
        var share = new FileShare
        {
            FileId = file.Id,
            ShareToken = Guid.NewGuid().ToString("N"),
            RequirePassword = true,
            PasswordHash = LegacyHash(password),
            IsEnabled = true,
        };
        DbContext.FileShares.Add(share);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return share;
    }

    private async Task<string?> StoredHashAsync(Guid shareId)
        => (await DbContext.FileShares.AsNoTracking().SingleAsync(s => s.Id == shareId)).PasswordHash;

    [Fact]
    public async Task ARightPassword_OnALegacyHash_StillOpensTheLink_AndUpgradesTheStoredHash()
    {
        var share = await SeedLegacyShareAsync("old-secret");
        var shares = CreateShareService();

        var opened = await shares.ValidateShareAccessAsync(share.ShareToken, "old-secret");

        Assert.True(opened.Data);
        var stored = await StoredHashAsync(share.Id);
        Assert.StartsWith("pbkdf2$", stored);
        Assert.True(SharePasswordHasher.Verify("old-secret", stored!, out var rehash));
        Assert.False(rehash, "once upgraded the hash is current");
    }

    /// <summary>升级是一次性的：第二次验证不再改写（写一次是升级，每次都写是无谓的 I/O）。</summary>
    [Fact]
    public async Task TheUpgradeHappensOnce()
    {
        var share = await SeedLegacyShareAsync("old-secret");
        var shares = CreateShareService();

        Assert.True((await shares.ValidateShareAccessAsync(share.ShareToken, "old-secret")).Data);
        var afterFirst = await StoredHashAsync(share.Id);
        Assert.True((await shares.ValidateShareAccessAsync(share.ShareToken, "old-secret")).Data);
        var afterSecond = await StoredHashAsync(share.Id);

        Assert.Equal(afterFirst, afterSecond);
    }

    /// <summary>错口令既不放行也不升级 —— 升级不能成为不带口令改写哈希的旁路。</summary>
    [Fact]
    public async Task AWrongPassword_OnALegacyHash_NeitherOpensNorUpgrades()
    {
        var share = await SeedLegacyShareAsync("old-secret");
        var shares = CreateShareService();

        var opened = await shares.ValidateShareAccessAsync(share.ShareToken, "nope");

        Assert.False(opened.Data);
        var stored = await StoredHashAsync(share.Id);
        Assert.Contains(':', stored!);
        Assert.DoesNotContain("pbkdf2", stored);
    }

    /// <summary>
    /// ★★ 升级只改哈希这一列：口令通过之后、升级写下之前，链接被停用了（并发的失败把它停掉，或创建者撤销了它），
    /// 升级不能把它写回启用。此前升级读出整行改完再整行写回，<c>IsEnabled</c> 与两个计数都被写回读到的旧值。
    /// </summary>
    [Fact]
    public async Task TheUpgrade_DoesNotReEnableALinkDisabledInBetween()
    {
        var share = await SeedLegacyShareAsync("old-secret");
        _hook.Arm("\"PasswordHash\"", $"UPDATE \"{ShareTable}\" SET \"IsEnabled\" = 0;");

        var opened = await CreateShareService().ValidateShareAccessAsync(share.ShareToken, "old-secret");

        Assert.True(_hook.Fired, "the concurrent disable was never injected");
        Assert.True(opened.Data);
        var stored = await DbContext.FileShares.AsNoTracking().SingleAsync(s => s.Id == share.Id);
        Assert.False(stored.IsEnabled);
        Assert.StartsWith("pbkdf2$", stored.PasswordHash);
    }

    [Fact]
    public async Task ANewShare_StoresAPbkdf2Hash()
    {
        var file = await CreateStoredFileAsync("fresh.txt", "bytes"u8.ToArray());
        var created = (await CreateShareService().CreateShareAsync(file.Id, password: "s3cret")).Data!;

        var stored = await StoredHashAsync(created.Id);
        Assert.StartsWith("pbkdf2$", stored);
    }

    /// <summary>
    /// 在第一条写到某一列的 UPDATE 执行之前，在同一个连接与事务上先执行一段 SQL：模拟一次刚刚提交的并发修改。只触发一次。
    /// </summary>
    private sealed class BeforeUpdateHook : DbCommandInterceptor
    {
        private string? _column;
        private string? _sql;

        public bool Fired { get; private set; }

        public void Arm(string column, string sql)
        {
            _column = column;
            _sql = sql;
            Fired = false;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await FireAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await FireAsync(command, cancellationToken);
            return result;
        }

        private async Task FireAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (_sql == null
                || !command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                || !command.CommandText.Contains(_column!, StringComparison.Ordinal))
            {
                return;
            }

            var sql = _sql;
            _sql = null;

            await using var injected = command.Connection!.CreateCommand();
            injected.Transaction = command.Transaction;
            injected.CommandText = sql;
            await injected.ExecuteNonQueryAsync(cancellationToken);
            Fired = true;
        }
    }
}
