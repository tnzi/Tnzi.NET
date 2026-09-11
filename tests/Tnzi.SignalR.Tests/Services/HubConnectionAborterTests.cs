using Tnzi.SignalR.Tests.TestDoubles;

namespace Tnzi.SignalR.Tests.Services;

/// <summary>
/// <see cref="HubConnectionAborter"/>：强制下线真正断掉传输的那一半。
///
/// ★ 清掉 <see cref="IConnectionManager"/> 的登记**不会**动到套接字。只做那一半时，
/// 被"强制下线"的客户端照常连着、照常收广播，只是从管理界面消失了；更糟的是连接计数
/// 被清零，同一个用户可以在那些还活着的连接之上再开满一整份配额。
/// </summary>
public class HubConnectionAborterTests
{
    private static HubConnectionAborter Create() =>
        new(Mock.Of<ILogger<HubConnectionAborter>>());

    [Fact]
    public void Abort_AbortsTheRegisteredConnection()
    {
        var aborter = Create();
        var context = new FakeHubCallerContext("conn-1");
        aborter.Register("conn-1", context);

        aborter.Abort("conn-1").ShouldBeTrue();

        context.AbortCount.ShouldBe(1);
        context.ConnectionAborted.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public void Abort_ReturnsFalseForAConnectionThisInstanceDoesNotHold()
    {
        Create().Abort("conn-unknown").ShouldBeFalse();
    }

    /// <summary>
    /// 中断后条目立即移除 —— 重复调用不得对同一条连接再发一次中断。
    /// </summary>
    [Fact]
    public void Abort_IsNotRepeatable()
    {
        var aborter = Create();
        var context = new FakeHubCallerContext("conn-1");
        aborter.Register("conn-1", context);

        aborter.Abort("conn-1").ShouldBeTrue();
        aborter.Abort("conn-1").ShouldBeFalse();

        context.AbortCount.ShouldBe(1);
        aborter.RegisteredCount.ShouldBe(0);
    }

    [Fact]
    public void AbortRange_AbortsEveryKnownConnectionAndCountsOnlyThose()
    {
        var aborter = Create();
        var first = new FakeHubCallerContext("conn-1");
        var second = new FakeHubCallerContext("conn-2");
        aborter.Register("conn-1", first);
        aborter.Register("conn-2", second);

        var aborted = aborter.AbortRange(["conn-1", "conn-2", "conn-elsewhere"]);

        aborted.ShouldBe(2);
        first.AbortCount.ShouldBe(1);
        second.AbortCount.ShouldBe(1);
    }

    [Fact]
    public void Unregister_LeavesTheConnectionAlone()
    {
        var aborter = Create();
        var context = new FakeHubCallerContext("conn-1");
        aborter.Register("conn-1", context);

        aborter.Unregister("conn-1");

        aborter.Abort("conn-1").ShouldBeFalse();
        context.AbortCount.ShouldBe(0);
    }

    [Fact]
    public void Register_IsIdempotentPerConnectionId()
    {
        var aborter = Create();
        aborter.Register("conn-1", new FakeHubCallerContext("conn-1"));
        aborter.Register("conn-1", new FakeHubCallerContext("conn-1"));

        aborter.RegisteredCount.ShouldBe(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankConnectionIdsAreNoOps(string connectionId)
    {
        var aborter = Create();

        aborter.Abort(connectionId).ShouldBeFalse();
        Should.NotThrow(() => aborter.Unregister(connectionId));
    }
}
