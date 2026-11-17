using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tnzi.Identity.IntegrationTests.Data;

/// <summary>
/// Identity 上下文发往 PostgreSQL 的命令里没有 U+0000：与 TnziDbContext 一样，字符串参数里的 NUL 在执行前换成 U+FFFD。
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL 的文本列拒收 NUL（SQLSTATE 22021）。这个替换此前只挂在 TnziDbContext 上，而 IdentityDbContext
/// 继承的是 ASP.NET Core Identity 的上下文、不经 TnziDbContext，于是应用的主上下文（多半继承 IdentityDbContext）
/// 一条带 NUL 的写入照旧失败：外部 CLI 运行的一条事件就因工具输出里的一个 NUL 记不下来。
/// </para>
/// <para>
/// 不连库：连接的打开被拦下，命令在所有拦截器之后由最后一个拦截器读出参数、以「影响 1 行」结束，
/// 看到的就是本来要发给 PostgreSQL 的值。对照组（ASP.NET Core 自己的 Identity 上下文）证明这个读法看得见 NUL。
/// </para>
/// </remarks>
public class IdentityDbContextNulCharacterTests
{
    private const string WithNul = "before\0after";
    private const string Sent = "before�after";

    [Fact]
    public async Task TnziIdentityContext_SendsTheReplacementCharacterToPostgreSql()
    {
        var recorder = new LastCommandRecorder();
        await using var context = new ProbeTnziIdentityContext(PostgreSqlOptions<ProbeTnziIdentityContext>(), recorder);

        await UpdateUserNameAsync(context.Users, WithNul);

        Assert.Contains(Sent, recorder.StringParameters);
        Assert.DoesNotContain(recorder.StringParameters, value => value.Contains('\0'));
    }

    /// <summary>对照组：同一个读法，换成不经 Tnzi 基类的 Identity 上下文，命令里原样带着 NUL。</summary>
    [Fact]
    public async Task PlainIdentityContext_SendsTheNulUnchanged()
    {
        var recorder = new LastCommandRecorder();
        await using var context = new ProbePlainIdentityContext(PostgreSqlOptions<ProbePlainIdentityContext>(), recorder);

        await UpdateUserNameAsync(context.Users, WithNul);

        Assert.Contains(WithNul, recorder.StringParameters);
    }

    private static Task<int> UpdateUserNameAsync<TUser>(DbSet<TUser> users, string userName)
        where TUser : Microsoft.AspNetCore.Identity.IdentityUser<Guid>
        => users.Where(u => u.Id == Guid.Empty).ExecuteUpdateAsync(s => s.SetProperty(u => u.UserName, userName));

    private static DbContextOptions<TContext> PostgreSqlOptions<TContext>() where TContext : DbContext
        => new DbContextOptionsBuilder<TContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=never_opened")
            .Options;

    /// <summary>
    /// 不打开连接、不执行命令：记下命令的字符串参数，以「影响 1 行」结束。
    /// 必须在上下文的其它拦截器之后登记（派生上下文的 <c>OnConfiguring</c> 在 <c>base</c> 之后加），读到的才是最终要发出的值。
    /// </summary>
    private sealed class LastCommandRecorder : DbCommandInterceptor, IDbConnectionInterceptor
    {
        public List<string> StringParameters { get; } = [];

        public ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult.Suppress());

        public ValueTask<InterceptionResult> ConnectionClosingAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            => ValueTask.FromResult(InterceptionResult.Suppress());

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            StringParameters.AddRange(command.Parameters.Cast<DbParameter>().Select(p => p.Value).OfType<string>());
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
        }
    }

    private sealed class ProbeTnziIdentityContext(DbContextOptions<ProbeTnziIdentityContext> options, LastCommandRecorder recorder)
        : IdentityDbContext<ProbeTnziIdentityContext>(options, new Mock<ICurrentUser>().Object)
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            optionsBuilder.AddInterceptors(recorder);
        }
    }

    private sealed class ProbePlainIdentityContext(DbContextOptions<ProbePlainIdentityContext> options, LastCommandRecorder recorder)
        : Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext<Microsoft.AspNetCore.Identity.IdentityUser<Guid>, Microsoft.AspNetCore.Identity.IdentityRole<Guid>, Guid>(options)
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            optionsBuilder.AddInterceptors(recorder);
        }
    }
}
