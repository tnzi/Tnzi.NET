using Npgsql;
using Tnzi.TestBase;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// PostgreSQL 的文本列存不了 U+0000：Tnzi 的上下文把它换成 U+FFFD 再发出去，写入、条件更新与查询都不因一个 NUL 失败。
/// </summary>
/// <remarks>
/// <para>
/// 此前一段带 NUL 的外来文本（读二进制文件、进程的原始输出）让整次写入以 SQLSTATE 22021
/// （<c>invalid byte sequence for encoding "UTF8": 0x00</c>）失败，调用方只拿到「An error occurred while saving the entity changes」。
/// </para>
/// <para>
/// 写库的用例只在 <see cref="PostgreSqlFactAttribute.ConnectionStringVariable"/> 给出 PostgreSQL 服务器时运行：
/// SQLite 收得下 NUL，在它上面这些用例去掉修复也是绿的。对照组（不是 Tnzi 的上下文）证明失败确实来自数据库。
/// </para>
/// </remarks>
public class PostgreSqlNulCharacterTests
{
    private const string WithNul = "before\0after";
    private const string Stored = "before�after";

    [PostgreSqlFact]
    public async Task SaveChanges_TextWithNul_IsStoredWithTheReplacementCharacter()
    {
        await using var context = await CreatePostgreSqlAsync();
        try
        {
            var probe = new NulProbe { Text = WithNul, Note = "\0", Tags = ["plain", "tag\0"] };
            context.Probes.Add(probe);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var stored = await context.Probes.AsNoTracking().SingleAsync(p => p.Id == probe.Id);
            Assert.Equal(Stored, stored.Text);
            Assert.Equal("�", stored.Note);
            Assert.Equal(["plain", "tag�"], stored.Tags);
            Assert.Equal(WithNul, probe.Text);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    [PostgreSqlFact]
    public async Task ExecuteUpdate_AndAQueryForTheSameText_BothSucceed()
    {
        await using var context = await CreatePostgreSqlAsync();
        try
        {
            var probe = new NulProbe { Text = "clean" };
            context.Probes.Add(probe);
            await context.SaveChangesAsync();

            var text = WithNul;
            var updated = await context.Probes.Where(p => p.Id == probe.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Text, text));
            Assert.Equal(1, updated);

            // 查询参数同样替换：按写进去的原值查得到它
            var found = await context.Probes.AsNoTracking().Where(p => p.Text == text).Select(p => p.Id).ToListAsync();
            Assert.Equal([probe.Id], found);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    /// <summary>对照组：同一张表、同一条写入，换一个不是 Tnzi 的上下文，PostgreSQL 照旧拒收。</summary>
    [PostgreSqlFact]
    public async Task WithoutTheTnziContext_PostgreSqlRejectsTheSameWrite()
    {
        await using var context = await CreatePostgreSqlAsync();
        try
        {
            await using var plain = new PlainProbeContext(
                new DbContextOptionsBuilder<PlainProbeContext>().UseNpgsql(context.Database.GetConnectionString()).Options);
            plain.Probes.Add(new NulProbe { Text = WithNul });

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => plain.SaveChangesAsync());
            Assert.Equal("22021", Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    /// <summary>别的数据库装得下 NUL，原样存、原样读回。</summary>
    [Fact]
    public async Task OnSqlite_TheTextIsStoredUnchanged()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new NulProbeContext(
            new DbContextOptionsBuilder<NulProbeContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();

        var probe = new NulProbe { Text = WithNul };
        context.Probes.Add(probe);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        Assert.Equal(WithNul, (await context.Probes.AsNoTracking().SingleAsync()).Text);
    }

    private static async Task<NulProbeContext> CreatePostgreSqlAsync()
    {
        var context = new NulProbeContext(
            new DbContextOptionsBuilder<NulProbeContext>().UseNpgsql(PostgreSqlFactAttribute.NewDatabase("tnzi_nul")).Options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    public sealed class NulProbe
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Text { get; set; } = string.Empty;

        public string? Note { get; set; }

        public List<string> Tags { get; set; } = [];
    }

    private static void ConfigureProbe(ModelBuilder modelBuilder, bool sqlite)
    {
        modelBuilder.Entity<NulProbe>(entity =>
        {
            entity.ToTable("NulProbes");
            entity.HasKey(p => p.Id);
            if (sqlite)
            {
                // SQLite 没有数组类型：这一组只看 Text
                entity.Ignore(p => p.Tags);
            }
        });
    }

    private sealed class NulProbeContext(DbContextOptions<NulProbeContext> options)
        : TnziDbContext<NulProbeContext>(options, new MockCurrentUser())
    {
        public DbSet<NulProbe> Probes => Set<NulProbe>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ConfigureProbe(modelBuilder, sqlite: Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) == true);
            base.OnModelCreating(modelBuilder);
        }
    }

    private sealed class PlainProbeContext(DbContextOptions<PlainProbeContext> options) : DbContext(options)
    {
        public DbSet<NulProbe> Probes => Set<NulProbe>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) => ConfigureProbe(modelBuilder, sqlite: false);
    }
}
