namespace Tnzi.AI.Tests.Services;

/// <summary>
/// How the per-turn metadata of several queued messages becomes the metadata of the one turn they go out as: keys listed in
/// <c>AI:Chat:Queue:StrictestValues</c> take the strictest value any of them carried (a value the ranking does not know
/// counts as strictest), every other key the one written last.
/// </summary>
public class ConfiguredChatQueueMetadataMergerTests
{
    private static readonly Dictionary<string, List<string>> PermissionRanking = new(StringComparer.Ordinal)
    {
        ["permissionMode"] = ["plan", "ask", "full"]
    };

    [Fact]
    public void ARankedKey_TakesTheStrictestValue_WhicheverOrderItWasWritten()
    {
        Merge(PermissionRanking, M(("permissionMode", "\"plan\"")), M(("permissionMode", "\"full\"")))!["permissionMode"].GetString().ShouldBe("plan");
        Merge(PermissionRanking, M(("permissionMode", "\"full\"")), M(("permissionMode", "\"ask\"")))!["permissionMode"].GetString().ShouldBe("ask");
    }

    [Fact]
    public void AValueTheRankingDoesNotKnow_CountsAsStrictest()
    {
        Merge(PermissionRanking, M(("permissionMode", "\"locked\"")), M(("permissionMode", "\"full\"")))!["permissionMode"].GetString()
            .ShouldBe("locked", "an unknown value overridden by 'full' is exactly the bypass this merge exists to stop");
        Merge(PermissionRanking, M(("permissionMode", "3")), M(("permissionMode", "\"plan\"")))!["permissionMode"].ValueKind
            .ShouldBe(JsonValueKind.Number);
    }

    [Fact]
    public void OtherKeys_FollowTheOrderWritten_AndMessagesWithoutTheKeyDoNotTakePart()
    {
        var merged = Merge(PermissionRanking,
            M(("permissionMode", "\"ask\""), ("device", "\"a\"")),
            M(),
            M(("device", "\"b\"")))!;

        merged["device"].GetString().ShouldBe("b");
        merged["permissionMode"].GetString().ShouldBe("ask");
    }

    [Fact]
    public void WithoutARanking_TheValueWrittenLastWins_AndNothingMergesToNull()
    {
        Merge(null, M(("permissionMode", "\"plan\"")), M(("permissionMode", "\"full\"")))!["permissionMode"].GetString().ShouldBe("full");
        Merge(PermissionRanking, M(), M()).ShouldBeNull();
    }

    [Fact]
    public async Task TheService_ReadsTheRankingFromOptions()
    {
        var options = new AiChatOptions();
        options.Queue.StrictestValues["permissionMode"] = ["plan", "ask", "full"];
        var monitor = new Mock<IOptionsMonitor<AiChatOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);

        var merged = await new ConfiguredChatQueueMetadataMerger(monitor.Object).MergeAsync(
            new ChatQueueMergeContext(Guid.NewGuid(), Guid.NewGuid(), null, [M(("permissionMode", "\"plan\"")), M(("permissionMode", "\"full\""))]));

        merged!["permissionMode"].GetString().ShouldBe("plan");
    }

    [Fact]
    public void TheValidator_RefusesAnEmptyOrRepeatingRanking()
    {
        var options = new AiChatOptions();
        options.Queue.StrictestValues["empty"] = [];
        options.Queue.StrictestValues["twice"] = ["plan", "plan"];

        var result = new AiChatOptionsValidator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("StrictestValues:empty");
        result.FailureMessage.ShouldContain("StrictestValues:twice");
    }

    private static IReadOnlyDictionary<string, JsonElement>? Merge(
        Dictionary<string, List<string>>? ranking, params IReadOnlyDictionary<string, JsonElement>[] parts)
        => ConfiguredChatQueueMetadataMerger.Merge(parts, ranking);

    private static IReadOnlyDictionary<string, JsonElement> M(params (string Key, string Json)[] entries)
        => entries.ToDictionary(e => e.Key, e => JsonDocument.Parse(e.Json).RootElement.Clone(), StringComparer.Ordinal);
}
