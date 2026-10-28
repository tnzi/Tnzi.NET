using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tnzi.Results;
using Tnzi.Security.Claims;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 收件人行的每一处写入只写它自己的那几列，不把读出来的快照整行写回。
/// </summary>
/// <remarks>
/// <para>
/// 被保护的缺陷：顺序签署唤醒下一位、首次打开记查看时间、提交、拒签，都是先读出收件人行、改几个字段、
/// 再 <c>UpdateAsync</c> 整行写回。读写之间管理端作废了这份请求（<c>TokenHash</c> 置空）的话，
/// 整行写回把旧的令牌哈希写回去：作废之后的链接重新解析得出，答的是一份只有状态的数据包而不是 404。
/// 反过来，作废自己也是读出再整行写回：读写之间落下的一次签署会被它改回「未签」。
/// </para>
/// <para>
/// 这里在被测写入<b>执行之前</b>插入那次并发写（同一连接，紧挨着那条 UPDATE），正是并发写落进读写窗口的形状。
/// </para>
/// </remarks>
public class SignerColumnWriteRaceTests : SigningStorageTestBase
{
    // 字段初始化先于基类构造函数执行，ConfigureDbContextOptions 被调用时它已经就位
    private readonly SignerUpdateInterleaver _interleaver = new();

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(_interleaver);

    protected override void ConfigureServices(IServiceCollection services)
    {
        var anonymous = new Mock<ICurrentUser>();
        anonymous.SetupGet(u => u.IsAuthenticated).Returns(false);
        anonymous.SetupGet(u => u.Id).Returns((Guid?)null);
        services.AddScoped(_ => anonymous.Object);
    }

    private static List<CreateSignerDto> TwoParties() =>
    [
        new CreateSignerDto { Role = "Buyer", Name = "Alice" },
        new CreateSignerDto { Role = "Seller", Name = "Bob" },
    ];

    private async Task VoidAsync(Guid requestId)
    {
        using var admin = BeginRequest();
        (await admin.Envelopes.VoidAsync(requestId)).Succeeded.ShouldBeTrue();
    }

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

    private async Task ShouldNotResolveAsync(string token)
    {
        using var request = BeginRequest();
        var packet = await request.Envelopes.GetByTokenAsync(token);
        packet.Succeeded.ShouldBeFalse();
        packet.Code.ShouldBe(404);
    }

    [Fact]
    public async Task Waking_the_next_signer_does_not_restore_a_token_revoked_by_a_concurrent_void()
    {
        var (_, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(recipients: TwoParties());

        // 第一次收件人写入是 Alice 自己的签署，第二次是唤醒 Bob：作废落在唤醒之前。
        _interleaver.Arm(2, () => VoidAsync(requestId));
        using (var submit = BeginRequest())
        {
            await submit.Envelopes.SubmitAsync(tokens[0], new SubmitSigningDto());
        }
        _interleaver.Ran.ShouldBeTrue("the void must have landed between the read and the wake-up write");

        var bob = await ReadSignerAsync(requestId, "Bob");
        bob.TokenHash.ShouldBeNull("a token cleared by a void must not be written back from a stale snapshot");
        bob.Status.ShouldBe(SigningRecipientStatus.Pending, "a revoked recipient has no usable link to wake");
        await ShouldNotResolveAsync(tokens[1]);
    }

    /// <summary>对照：没有并发作废时，顺序签署照常唤醒下一位，他的链接可用。</summary>
    [Fact]
    public async Task Without_a_void_the_next_signer_is_woken_and_keeps_a_working_link()
    {
        var (_, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(recipients: TwoParties());

        using (var submit = BeginRequest())
        {
            (await submit.Envelopes.SubmitAsync(tokens[0], new SubmitSigningDto())).Succeeded.ShouldBeTrue();
        }

        var bob = await ReadSignerAsync(requestId, "Bob");
        bob.Status.ShouldBe(SigningRecipientStatus.Sent);
        bob.SentAt.ShouldNotBeNull();
        bob.TokenHash.ShouldNotBeNull();

        using var request = BeginRequest();
        var packet = await request.Envelopes.GetByTokenAsync(tokens[1]);
        packet.Succeeded.ShouldBeTrue(packet.Message);
        packet.Data!.IsMyTurn.ShouldBeTrue();
    }

    [Fact]
    public async Task Signing_does_not_restore_the_signers_own_token_revoked_by_a_concurrent_void()
    {
        var (_, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(recipients: TwoParties());

        _interleaver.Arm(1, () => VoidAsync(requestId));
        Result<SigningPacketDto> result;
        using (var submit = BeginRequest())
        {
            result = await submit.Envelopes.SubmitAsync(tokens[0], new SubmitSigningDto());
        }
        _interleaver.Ran.ShouldBeTrue();

        // 作废落在过门之后、写入之前：这一签不再落库，答复与作废之后再打开链接同一句。
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        var alice = await ReadSignerAsync(requestId, "Alice");
        alice.TokenHash.ShouldBeNull();
        alice.Status.ShouldNotBe(SigningRecipientStatus.Signed, "a signature must not land on a request voided after the gate");
        alice.SignedAt.ShouldBeNull();
        await ShouldNotResolveAsync(tokens[0]);
    }

    [Fact]
    public async Task Declining_does_not_restore_a_token_revoked_by_a_concurrent_void()
    {
        var (_, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(recipients: TwoParties());

        // 只清令牌、不动信封：看收件人行的那一条写入，以及它没占到时信封不被碰。
        _interleaver.Arm(1, () => RevokeTokensAsync(requestId));
        Result<SigningPacketDto> result;
        using (var decline = BeginRequest())
        {
            result = await decline.Envelopes.DeclineAsync(tokens[0], "Not today");
        }
        _interleaver.Ran.ShouldBeTrue();

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        var alice = await ReadSignerAsync(requestId, "Alice");
        alice.Status.ShouldNotBe(SigningRecipientStatus.Declined, "a revoked link must not record a decline");
        alice.TokenHash.ShouldBeNull();
        (await ReadEnvelopeStatusAsync(requestId)).ShouldBe(EnvelopeStatus.Sent);
        await ShouldNotResolveAsync(tokens[0]);
    }

    /// <summary>
    /// 同一链接的两次提交（双击、超时重试）只有先到的那次落库：后到的那次不能改掉先到那次的签署记录与字段值。
    /// </summary>
    /// <remarks>
    /// 密封盖进成品的是先到那次的签名与取值；后到的那次若照样写下自己的，库里的签署记录与成品就对不上了。
    /// </remarks>
    [Fact]
    public async Task A_second_submission_through_the_same_link_does_not_overwrite_the_first()
    {
        var (_, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(recipients: TwoParties());

        // 先到的那次整个插进后到那次的「过门之后、写入之前」。
        _interleaver.Arm(1, async () =>
        {
            using var first = BeginRequest();
            var landed = await first.Envelopes.SubmitAsync(tokens[0], new SubmitSigningDto { ConsentText = "first" });
            landed.Succeeded.ShouldBeTrue(landed.Message);
        });
        Result<SigningPacketDto> second;
        using (var submit = BeginRequest())
        {
            second = await submit.Envelopes.SubmitAsync(tokens[0], new SubmitSigningDto { ConsentText = "second" });
        }
        _interleaver.Ran.ShouldBeTrue();

        second.Succeeded.ShouldBeFalse();
        second.Code.ShouldBe(409);
        second.Message!.ShouldContain("already signed");
        var alice = await ReadSignerAsync(requestId, "Alice");
        alice.Status.ShouldBe(SigningRecipientStatus.Signed);
        alice.ConsentText.ShouldBe("first");
    }

    /// <summary>
    /// 另一位收件人在过门之后、写入之前拒签了：请求已是 Declined，这一签不再落库。
    /// </summary>
    [Fact]
    public async Task A_signature_does_not_land_on_a_request_another_recipient_declined_after_the_gate()
    {
        var (_, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(recipients: TwoParties());

        _interleaver.Arm(1, async () =>
        {
            using var bob = BeginRequest();
            (await bob.Envelopes.DeclineAsync(tokens[1], "Wrong terms")).Succeeded.ShouldBeTrue();
        });
        Result<SigningPacketDto> result;
        using (var submit = BeginRequest())
        {
            result = await submit.Envelopes.SubmitAsync(tokens[0], new SubmitSigningDto());
        }
        _interleaver.Ran.ShouldBeTrue();

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        (await ReadSignerAsync(requestId, "Alice")).Status.ShouldNotBe(SigningRecipientStatus.Signed);
        (await ReadEnvelopeStatusAsync(requestId)).ShouldBe(EnvelopeStatus.Declined);
    }

    [Fact]
    public async Task Recording_the_first_view_does_not_restore_a_token_revoked_by_a_concurrent_void()
    {
        var (_, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(recipients: TwoParties());

        _interleaver.Arm(1, () => VoidAsync(requestId));
        using (var open = BeginRequest())
        {
            (await open.Envelopes.GetByTokenAsync(tokens[0])).Succeeded.ShouldBeTrue();
        }
        _interleaver.Ran.ShouldBeTrue();

        (await ReadSignerAsync(requestId, "Alice")).TokenHash.ShouldBeNull();
        await ShouldNotResolveAsync(tokens[0]);
    }

    /// <summary>
    /// 反方向：作废读出收件人之后、写回之前落下的一次签署，不能被作废的旧快照改回「未签」。
    /// </summary>
    [Fact]
    public async Task Voiding_does_not_undo_a_signature_that_landed_after_it_read_the_recipients()
    {
        var (_, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(recipients: TwoParties());
        var alice = await ReadSignerAsync(requestId, "Alice");

        _interleaver.Arm(1, () => MarkSignedAsync(alice.Id));
        await VoidAsync(requestId);
        _interleaver.Ran.ShouldBeTrue();

        var after = await ReadSignerAsync(requestId, "Alice");
        after.Status.ShouldBe(SigningRecipientStatus.Signed);
        after.TokenHash.ShouldBeNull();
        await ShouldNotResolveAsync(tokens[0]);
    }

    /// <summary>只清掉这份请求的全部令牌（作废的收件人那一半），不动信封。</summary>
    private Task RevokeTokensAsync(Guid requestId)
        => ExecuteSqlAsync(table => $"UPDATE \"{table}\" SET \"TokenHash\" = NULL WHERE \"RequestId\" = $id", requestId);

    /// <summary>直接把一名收件人记成已签：模拟一次并发落下的签署。</summary>
    private Task MarkSignedAsync(Guid signerId)
        => ExecuteSqlAsync(table => $"UPDATE \"{table}\" SET \"Status\" = {(int)SigningRecipientStatus.Signed} WHERE \"Id\" = $id", signerId);

    private async Task ExecuteSqlAsync(Func<string, string> sql, Guid id)
    {
        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SigningStorageDbContext>();
        var table = db.Model.FindEntityType(typeof(Signer))!.GetTableName();
        var connection = db.Database.GetDbConnection();

        await using var command = connection.CreateCommand();
        command.CommandText = sql(table!);
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$id";
        parameter.Value = id.ToString().ToUpperInvariant();
        command.Parameters.Add(parameter);
        (await command.ExecuteNonQueryAsync()).ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// 在武装之后的第 N 条收件人表 UPDATE 执行之前，跑一次给定的并发写。跑之前先解除武装（并发写自己也会写收件人表）。
    /// </summary>
    private sealed class SignerUpdateInterleaver : DbCommandInterceptor
    {
        private int _remaining;
        private Func<Task>? _action;

        public bool Ran { get; private set; }

        public void Arm(int nthSignerUpdate, Func<Task> action)
        {
            _remaining = nthSignerUpdate;
            _action = action;
            Ran = false;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await InterleaveAsync(command, eventData);
            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await InterleaveAsync(command, eventData);
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private async Task InterleaveAsync(DbCommand command, CommandEventData eventData)
        {
            if (_action == null || eventData.Context == null)
                return;

            var table = eventData.Context.Model.FindEntityType(typeof(Signer))!.GetTableName();
            if (!command.CommandText.Contains($"UPDATE \"{table}\"", StringComparison.Ordinal))
                return;

            if (--_remaining > 0)
                return;

            var action = _action;
            _action = null;
            await action();
            Ran = true;
        }
    }
}
