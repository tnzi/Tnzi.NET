namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// 条件边的路由键不区分大小写，无论边是怎么构造出来的。
/// </summary>
/// <remarks>
/// review / conditional 节点给出的 <c>RouteTo</c> 一律小写，而引擎按 <c>Routes.TryGetValue(RouteTo)</c> 查表。
/// 比较器此前取决于构造方：服务层保存时用了不区分大小写的字典，但比较器不随 JSON 存储，读回来就区分大小写；
/// 代码优先的 Builder 直接用调用方传入的字典。键写成 <c>Approve</c> 时每个裁决都静默落到默认目标。
/// </remarks>
public class ConditionalEdgeRouteKeyTests
{
    [Fact]
    public void Routes_GivenAnOrdinalDictionary_MatchInAnyCase()
    {
        var edge = new ConditionalEdge
        {
            FromNodeId = "review",
            Routes = new Dictionary<string, string> { ["Approve"] = "publish", ["Revise"] = "rework" }
        };

        edge.Routes.TryGetValue("revise", out var target).ShouldBeTrue();
        target.ShouldBe("rework");
    }

    [Fact]
    public void Routes_ReadBackFromJson_MatchInAnyCase()
    {
        var stored = JsonSerializer.Serialize(new ConditionalEdge
        {
            FromNodeId = "review",
            Routes = new Dictionary<string, string> { ["Approve"] = "publish" }
        }, TnziJsonDefaults.Options);

        var edge = JsonSerializer.Deserialize<ConditionalEdge>(stored, TnziJsonDefaults.Options)!;

        edge.Routes.ContainsKey("approve").ShouldBeTrue();
    }

    [Fact]
    public void Routes_FromTheBuilder_MatchInAnyCase()
    {
        var graph = WorkflowBuilder.Create()
            .AddReviewStep("review")
            .AddStep("publish").DependsOn("review")
            .AddStep("rework").DependsOn("review")
            .AddConditionalEdge("review", new Dictionary<string, string> { ["Approve"] = "publish", ["Revise"] = "rework" })
            .BuildGraph();

        graph.GetConditionalEdge("review")!.Routes.ContainsKey("approve").ShouldBeTrue();
    }

    [Fact]
    public void Routes_KeysThatDifferOnlyInCase_AreRejected()
    {
        Should.Throw<ArgumentException>(() => new ConditionalEdge
        {
            FromNodeId = "review",
            Routes = new Dictionary<string, string> { ["approve"] = "publish", ["Approve"] = "archive" }
        });
    }
}
