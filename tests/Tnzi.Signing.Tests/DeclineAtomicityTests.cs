using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tnzi.Data;
using Tnzi.EFCore;
using Tnzi.Results;
using Tnzi.Security.Claims;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 拒签的两次写入（收件人认领、信封改成 Declined）要么一起生效要么一起不生效。
/// </summary>
/// <remarks>
/// <para>
/// 被保护的缺陷：认领先自动提交，信封那一写带乐观并发戳。两次写入之间落下一次并发写（作废、另一位收件人拒签、
/// 并行签署的推进），信封那一写撞上并发冲突，拒签答 500，收件人却停在 Declined 而信封已经作废。
/// </para>
/// <para>
/// 这一组装上真实的 <see cref="UnitOfWorkManager"/>（生产里由 EFCore 模块注册），拒签的两次写入落在同一个事务里。
/// SQLite 同一时刻只有一个写者，另一条连接提交不进一个开着的写事务，所以并发写分两步模拟：
/// 先在拒签自己的连接与事务上、紧挨着信封那条 UPDATE 之前执行一遍（推进并发戳，信封那一写因此一行都没写到）；
/// 拒签的事务回滚之后再在事务之外执行一遍，代表那次并发写在服务器数据库上已经提交。
/// 于是拒签之后读到的库与服务器上一致，答复按「库里此刻的状态」来核对：链接被吊销答 404，请求不在进行中答 409，
/// 冲突来自一次无关的推进时答「请重试」，重试成功。
/// </para>
/// </remarks>
public class DeclineAtomicityTests : SigningStorageTestBase
{
    // 字段初始化先于基类构造函数执行，ConfigureDbContextOptions 被调用时它已经就位
    private readonly ConcurrentEnvelopeWrite _concurrent = new();

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(new InterleaveBeforeEnvelopeUpdate(_concurrent), new CommitAfterRollback(_concurrent));

    protected override void ConfigureServices(IServiceCollection services)
    {
        var anonymous = new Mock<ICurrentUser>();
        anonymous.SetupGet(u => u.IsAuthenticated).Returns(false);
        anonymous.SetupGet(u => u.Id).Returns((Guid?)null);
        services.AddScoped(_ => anonymous.Object);

        // 真实的工作单元：拒签的两次写入落在同一个事务里（生产里由 EFCore 模块注册）
        var entityManager = new Mock<IEntityManager>();
        entityManager.Setup(m => m.GetAllDbContextTypes()).Returns([typeof(SigningStorageDbContext)]);
        services.AddSingleton(entityManager.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
    }

    private static List<CreateSignerDto> TwoParties() =>
    [
        new CreateSignerDto { Role = "Buyer", Name = "Alice" },
        new CreateSignerDto { Role = "Seller", Name = "Bob" },
    ];

    private async Task<Signer> ReadSignerAsync(Guid requestId, string name)
    {
        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SigningStorageDbContext>();
        return await db.Set<Signer>().AsNoTracking().SingleAsync(s => s.RequestId == requestId && s.Name == name);
    }

    private async Task<EnvelopeStatus> ReadEnvelopeStatusAsync(Guid requestId)
    {
        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SigningStorageDbContext>();
        return await db.Set<Envelope>().AsNoTracking().Where(e => e.Id == requestId).Select(e => e.Status).SingleAsync();
    }

    private async Task<Result<SigningPacketDto>> DeclineAsync(string token)
    {
        using var decline = BeginRequest();
        return await decline.Envelopes.DeclineAsync(token, "Wrong terms");
    }

    /// <summary>
    /// 与全局工作单元过滤器同一形状：动作之前开启一层外层工作单元，失败的结果回滚、成功的提交。
    /// </summary>
    private async Task<Result<SigningPacketDto>> DeclineInsideAnEnclosingUnitOfWorkAsync(string token)
    {
        using var decline = BeginRequest();
        var unitOfWork = decline.Services.GetRequiredService<IUnitOfWorkManager>();
        unitOfWork.EnableTransaction();

        var result = await decline.Envelopes.DeclineAsync(token, "Wrong terms");

        if (result.Succeeded)
            await unitOfWork.CommitTransactionAsync();
        else
            await unitOfWork.RollbackTransactionAsync();
        return result;
    }

    private async Task AssertTheVoidWonAsync(Guid requestId, Result<SigningPacketDto> result)
    {
        _concurrent.Interleaved.ShouldBeTrue("the void must have landed between the claim and the envelope write");
        _concurrent.Committed.ShouldBeTrue("the decline's transaction must have rolled back");

        // 冲突答成一次拒绝，不是 500；作废吊销了链接，与作废之后再打开链接同一句
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        result.Message.ShouldBe("This signing link is not valid.");

        // 收件人这一侧什么都没留下，信封是作废留下的样子
        var alice = await ReadSignerAsync(requestId, "Alice");
        alice.Status.ShouldNotBe(SigningRecipientStatus.Declined, "the decline must roll back with the envelope write");
        alice.DeclinedAt.ShouldBeNull();
        alice.DeclineReason.ShouldBeNull();
        alice.TokenHash.ShouldBeNull();
        (await ReadEnvelopeStatusAsync(requestId)).ShouldBe(EnvelopeStatus.Voided);
    }

    [Fact]
    public async Task A_void_landing_between_the_two_writes_leaves_no_declined_recipient()
    {
        var (_, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(recipients: TwoParties());

        _concurrent.Arm(requestId, ConcurrentEnvelopeWrite.Void);
        var result = await DeclineAsync(tokens[0]);

        await AssertTheVoidWonAsync(requestId, result);
    }

    /// <summary>
    /// 另一位收件人的拒签先提交：请求已不在进行中（409），这一位的拒签整体回滚，记下的拒签人只有先到的那一位。
    /// </summary>
    [Fact]
    public async Task Another_recipients_decline_landing_between_the_two_writes_is_the_only_decline_recorded()
    {
        var (_, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(recipients: TwoParties());

        _concurrent.Arm(requestId, ConcurrentEnvelopeWrite.DeclinedBy("Bob"));
        var result = await DeclineAsync(tokens[0]);
        _concurrent.Interleaved.ShouldBeTrue();
        _concurrent.Committed.ShouldBeTrue();

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        result.Message.ShouldBe("This request is no longer active.");
        (await ReadSignerAsync(requestId, "Alice")).Status.ShouldNotBe(SigningRecipientStatus.Declined);
        (await ReadSignerAsync(requestId, "Bob")).Status.ShouldBe(SigningRecipientStatus.Declined);
        (await ReadEnvelopeStatusAsync(requestId)).ShouldBe(EnvelopeStatus.Declined);
    }

    /// <summary>
    /// 信封在读出之后被一次无关的写入推进过（并行签署里另一位收件人签了）：拒签同样整体回滚，请求仍可签署所以答「请重试」，
    /// 重试成功，收件人与信封都记成拒签。
    /// </summary>
    [Fact]
    public async Task An_unrelated_envelope_write_between_the_two_writes_rolls_back_and_a_retry_succeeds()
    {
        var (_, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(recipients: TwoParties());

        _concurrent.Arm(requestId, ConcurrentEnvelopeWrite.Advance);
        var first = await DeclineAsync(tokens[0]);
        _concurrent.Interleaved.ShouldBeTrue();
        _concurrent.Committed.ShouldBeTrue();

        first.Succeeded.ShouldBeFalse();
        first.Code.ShouldBe(409);
        first.Message.ShouldBe("The request changed while the decline was being recorded. Please try again.");
        (await ReadSignerAsync(requestId, "Alice")).Status.ShouldNotBe(SigningRecipientStatus.Declined);
        (await ReadEnvelopeStatusAsync(requestId)).ShouldBe(EnvelopeStatus.InProgress);

        var retry = await DeclineAsync(tokens[0]);

        retry.Succeeded.ShouldBeTrue(retry.Message);
        (await ReadSignerAsync(requestId, "Alice")).Status.ShouldBe(SigningRecipientStatus.Declined);
        (await ReadEnvelopeStatusAsync(requestId)).ShouldBe(EnvelopeStatus.Declined);
    }

    /// <summary>
    /// 外层已有工作单元（开了全局工作单元过滤器的部署）：拒签的工作单元嵌套在里面，冲突时的回滚撤销整个环境事务，
    /// 拒签仍答成一次拒绝；外层随后按失败的结果回滚，收件人这一侧同样什么都没留下。
    /// </summary>
    [Fact]
    public async Task Inside_an_enclosing_unit_of_work_a_void_between_the_two_writes_still_leaves_no_declined_recipient()
    {
        var (_, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(recipients: TwoParties());

        _concurrent.Arm(requestId, ConcurrentEnvelopeWrite.Void);
        var result = await DeclineInsideAnEnclosingUnitOfWorkAsync(tokens[0]);

        await AssertTheVoidWonAsync(requestId, result);
    }

    /// <summary>对照：外层已有工作单元、没有并发写入时，拒签的两次写入随外层一起提交。</summary>
    [Fact]
    public async Task Inside_an_enclosing_unit_of_work_the_decline_commits_with_it()
    {
        var (_, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(recipients: TwoParties());

        var result = await DeclineInsideAnEnclosingUnitOfWorkAsync(tokens[0]);

        result.Succeeded.ShouldBeTrue(result.Message);
        (await ReadSignerAsync(requestId, "Alice")).Status.ShouldBe(SigningRecipientStatus.Declined);
        (await ReadEnvelopeStatusAsync(requestId)).ShouldBe(EnvelopeStatus.Declined);
    }

    /// <summary>对照：没有并发写入时，拒签在工作单元里提交，两边都记成拒签。</summary>
    [Fact]
    public async Task Without_a_concurrent_write_the_decline_commits_both_writes()
    {
        var (_, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(recipients: TwoParties());

        var result = await DeclineAsync(tokens[0]);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.RequestStatus.ShouldBe(EnvelopeStatus.Declined);
        var alice = await ReadSignerAsync(requestId, "Alice");
        alice.Status.ShouldBe(SigningRecipientStatus.Declined);
        alice.DeclineReason.ShouldBe("Wrong terms");
        (await ReadEnvelopeStatusAsync(requestId)).ShouldBe(EnvelopeStatus.Declined);
    }

    /// <summary>
    /// 一次与拒签并发的写入：一组按表名拼出的 SQL，<c>$id</c> 是请求的 Id。
    /// </summary>
    private sealed class ConcurrentEnvelopeWrite
    {
        /// <summary>作废：信封改成 Voided、推进并发戳，并吊销每个收件人的令牌（与 <c>VoidAsync</c> 写下的一样）。</summary>
        public static readonly Func<string, string, IReadOnlyList<string>> Void = (envelope, signer) =>
        [
            $"UPDATE \"{envelope}\" SET \"Status\" = {(int)EnvelopeStatus.Voided}, \"ConcurrencyStamp\" = 'concurrent-void' WHERE \"Id\" = $id",
            $"UPDATE \"{signer}\" SET \"TokenHash\" = NULL WHERE \"RequestId\" = $id",
        ];

        /// <summary>并行签署里另一位收件人签了：信封推进成 InProgress、推进并发戳。</summary>
        public static readonly Func<string, string, IReadOnlyList<string>> Advance = (envelope, _) =>
        [
            $"UPDATE \"{envelope}\" SET \"Status\" = {(int)EnvelopeStatus.InProgress}, \"ConcurrencyStamp\" = 'concurrent-advance' WHERE \"Id\" = $id",
        ];

        /// <summary>另一位收件人先拒签了：他记成 Declined，信封改成 Declined、推进并发戳。</summary>
        public static Func<string, string, IReadOnlyList<string>> DeclinedBy(string name) => (envelope, signer) =>
        [
            $"UPDATE \"{envelope}\" SET \"Status\" = {(int)EnvelopeStatus.Declined}, \"ConcurrencyStamp\" = 'concurrent-decline' WHERE \"Id\" = $id",
            $"UPDATE \"{signer}\" SET \"Status\" = {(int)SigningRecipientStatus.Declined} WHERE \"RequestId\" = $id AND \"Name\" = '{name}'",
        ];

        private Guid? _requestId;
        private Func<string, string, IReadOnlyList<string>>? _statements;
        private IReadOnlyList<string>? _awaitingCommit;
        private Guid _awaitingRequestId;

        /// <summary>并发写已在信封那条 UPDATE 之前、拒签的事务里执行过。</summary>
        public bool Interleaved { get; private set; }

        /// <summary>拒签的事务回滚之后，并发写已在事务之外执行（代表它在服务器上已经提交）。</summary>
        public bool Committed { get; private set; }

        public void Arm(Guid requestId, Func<string, string, IReadOnlyList<string>> statements)
        {
            _requestId = requestId;
            _statements = statements;
            _awaitingCommit = null;
            Interleaved = false;
            Committed = false;
        }

        public async Task InterleaveAsync(DbCommand command, CommandEventData eventData, CancellationToken cancellationToken)
        {
            if (_requestId is not { } requestId || _statements == null || eventData.Context == null)
                return;

            var (envelopeTable, signerTable) = TableNames(eventData.Context);
            if (!command.CommandText.Contains($"UPDATE \"{envelopeTable}\"", StringComparison.Ordinal))
                return;

            var statements = _statements(envelopeTable, signerTable);
            _requestId = null;
            _statements = null;

            await ExecuteAsync(command.Connection!, command.Transaction, statements, requestId, cancellationToken);
            Interleaved = true;

            // 在事务里执行的这一遍会随拒签的事务一起回滚；没有事务时（工作单元缺席）它已经生效，不必再提交一次
            if (command.Transaction != null)
            {
                _awaitingCommit = statements;
                _awaitingRequestId = requestId;
            }
        }

        public async Task CommitAfterRollbackAsync(DbContext? context, CancellationToken cancellationToken)
        {
            if (_awaitingCommit is not { } statements || context == null)
                return;

            _awaitingCommit = null;
            await ExecuteAsync(context.Database.GetDbConnection(), null, statements, _awaitingRequestId, cancellationToken);
            Committed = true;
        }

        private static (string Envelope, string Signer) TableNames(DbContext context)
            => (context.Model.FindEntityType(typeof(Envelope))!.GetTableName()!,
                context.Model.FindEntityType(typeof(Signer))!.GetTableName()!);

        private static async Task ExecuteAsync(
            DbConnection connection, DbTransaction? transaction, IReadOnlyList<string> statements, Guid requestId, CancellationToken cancellationToken)
        {
            foreach (var sql in statements)
            {
                await using var concurrent = connection.CreateCommand();
                concurrent.Transaction = transaction;
                concurrent.CommandText = sql;
                var parameter = concurrent.CreateParameter();
                parameter.ParameterName = "$id";
                parameter.Value = requestId.ToString().ToUpperInvariant();
                concurrent.Parameters.Add(parameter);
                (await concurrent.ExecuteNonQueryAsync(cancellationToken)).ShouldBeGreaterThan(0, sql);
            }
        }
    }

    /// <summary>武装之后，在信封表的第一条 UPDATE 执行之前，于同一连接、同一事务里执行那次并发写。</summary>
    private sealed class InterleaveBeforeEnvelopeUpdate(ConcurrentEnvelopeWrite concurrent) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await concurrent.InterleaveAsync(command, eventData, cancellationToken);
            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await concurrent.InterleaveAsync(command, eventData, cancellationToken);
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>拒签的事务回滚之后，把那次并发写在事务之外再执行一遍：它在服务器上是另一个已经提交的事务。</summary>
    private sealed class CommitAfterRollback(ConcurrentEnvelopeWrite concurrent) : DbTransactionInterceptor
    {
        public override async Task TransactionRolledBackAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            await concurrent.CommitAfterRollbackAsync(eventData.Context, cancellationToken);
            await base.TransactionRolledBackAsync(transaction, eventData, cancellationToken);
        }
    }
}
