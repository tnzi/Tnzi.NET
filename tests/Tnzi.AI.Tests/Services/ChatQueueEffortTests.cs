namespace Tnzi.AI.Tests.Services;

/// <summary>
/// 排队消息的推理强度：合成一轮时取哪一条的，以及一轮进行中能不能把它交给正在跑的那一轮。
/// </summary>
public class ChatQueueEffortTests
{
    private static QueuedThreadMessage Message(string? model = null, string? effort = null) => new()
    {
        Id = Guid.NewGuid(), ThreadId = Guid.NewGuid(), UserId = Guid.NewGuid(), Text = "x", Model = model, Effort = effort
    };

    [Fact]
    public void TheMergedTurn_TakesTheLastChosenEffort()
        => ChatQueueDelivery.MergedEffort([Message(effort: "low"), Message(), Message(effort: "high"), Message()]).ShouldBe("high");

    [Fact]
    public void AnEffortChosenBeforeALaterModel_IsDropped()
    {
        // 强度属于模型：第一条的 max 是按 Opus 选的，第二条换成了 Sonnet
        ChatQueueDelivery.MergedEffort([Message("claude-opus-5-5", "max"), Message("claude-sonnet-5-5")]).ShouldBeNull();
        ChatQueueDelivery.MergedEffort([Message("claude-opus-5-5", "max"), Message()]).ShouldBe("max");
        ChatQueueDelivery.MergedEffort([Message("claude-opus-5-5"), Message(effort: "low")]).ShouldBe("low");
    }

    [Theory]
    [InlineData(null, "high", true)]
    [InlineData("high", "high", true)]
    [InlineData("HIGH", "high", true)]
    [InlineData("low", "high", false)]
    [InlineData("low", null, false)]
    public async Task AMessageWithAnotherEffort_WaitsForTheTurnToEnd(string? effort, string? runEffort, bool goes)
    {
        var goesMidTurn = await ChatQueueHandover.CanGoMidTurnAsync(
            Message(effort: effort), hasFiles: false, Guid.NewGuid(), runEffort, new ChatQueueOptions(), [], CancellationToken.None);

        goesMidTurn.ShouldBe(goes);
    }

    [Fact]
    public async Task ThePolicy_SeesBothEfforts_AndDecides()
    {
        ChatQueueHandoverRequest? seen = null;
        var policy = new Mock<IChatQueueHandoverPolicy>();
        policy.Setup(p => p.CanHandOverAsync(It.IsAny<ChatQueueHandoverRequest>(), It.IsAny<CancellationToken>()))
            .Callback((ChatQueueHandoverRequest r, CancellationToken _) => seen = r)
            .ReturnsAsync(true);

        var goes = await ChatQueueHandover.CanGoMidTurnAsync(
            Message(effort: " low "), hasFiles: false, Guid.NewGuid(), "high", new ChatQueueOptions(), [policy.Object], CancellationToken.None);

        goes.ShouldBeTrue("an application may hand it over anyway; it is then answered at the turn's effort");
        seen.ShouldNotBeNull();
        seen.Effort.ShouldBe("low");
        seen.RunEffort.ShouldBe("high");
        seen.DefaultDecision.ShouldBeFalse();
    }
}
