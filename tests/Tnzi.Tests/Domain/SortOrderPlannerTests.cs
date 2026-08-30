using Tnzi.Domain.Entities;

namespace Tnzi.Tests.Domain;

/// <summary>
/// <see cref="SortOrderPlanner.Plan{TEntity,TKey}"/> 行为测试：槽位保留（提交子集不挤动范围外记录）、
/// 规划阶段无副作用、校验（空/重复/越界/序列自身重复）、只输出真正变化的赋值。
/// </summary>
public class SortOrderPlannerTests
{
    private sealed class Row : IHasOrder
    {
        public string Id { get; init; } = string.Empty;
        public int SortOrder { get; set; }
        public override string ToString() => $"{Id}@{SortOrder}";
    }

    private static List<Row> Sequence(params string[] ids) =>
        [.. ids.Select((id, i) => new Row { Id = id, SortOrder = i + 1 })];

    [Fact]
    public void Plan_FullSequenceSubmitted_RenumbersInSubmittedOrder()
    {
        var current = Sequence("a", "b", "c");

        var plan = SortOrderPlanner.Plan(current, ["c", "a", "b"], r => r.Id);

        Assert.True(plan.IsValid);
        Assert.Equal(["c", "a", "b"], plan.Ordered.Select(r => r.Id));
        plan.Apply();
        Assert.Equal(1, current.Single(r => r.Id == "c").SortOrder);
        Assert.Equal(2, current.Single(r => r.Id == "a").SortOrder);
        Assert.Equal(3, current.Single(r => r.Id == "b").SortOrder);
    }

    /// <summary>
    /// 核心不变量：提交的只是可见的一段（分页 / 筛选）时，未提交的记录必须留在原来的位置上。
    /// 若改成「给提交的这几条编 1..N」，下面的 c 会拿到 1、a 拿到 2，与原地不动的 b 的 2 相撞。
    /// </summary>
    [Fact]
    public void Plan_PartialSubmission_KeepsUnsubmittedRecordsInTheirSlots()
    {
        var current = Sequence("a", "b", "c", "d", "e");

        // 用户只看得见并拖动了 a 与 c（第 1 和第 3 个位置），把 c 拖到 a 前面。
        var plan = SortOrderPlanner.Plan(current, ["c", "a"], r => r.Id);

        Assert.True(plan.IsValid);
        Assert.Equal(["c", "b", "a", "d", "e"], plan.Ordered.Select(r => r.Id));

        plan.Apply();
        Assert.Equal(1, current.Single(r => r.Id == "c").SortOrder);
        Assert.Equal(2, current.Single(r => r.Id == "b").SortOrder); // 原地不动
        Assert.Equal(3, current.Single(r => r.Id == "a").SortOrder);
        Assert.Equal(4, current.Single(r => r.Id == "d").SortOrder);
        Assert.Equal(5, current.Single(r => r.Id == "e").SortOrder);

        // 撞号是这条规则要防的事故，单独断言一次序号全局唯一。
        Assert.Equal(current.Count, current.Select(r => r.SortOrder).Distinct().Count());
    }

    [Fact]
    public void Plan_PartialSubmission_NeverMovesRecordsOutsideTheSubmittedSlots()
    {
        var current = Sequence("a", "b", "c", "d");

        var plan = SortOrderPlanner.Plan(current, ["d", "b"], r => r.Id);

        Assert.True(plan.IsValid);
        // b 与 d 占的是第 2、第 4 位；a 与 c 必须仍在第 1、第 3 位。
        Assert.Equal(["a", "d", "c", "b"], plan.Ordered.Select(r => r.Id));
    }

    [Fact]
    public void Plan_DoesNotMutateEntities_UntilApplyIsCalled()
    {
        var current = Sequence("a", "b", "c");

        var plan = SortOrderPlanner.Plan(current, ["c", "b", "a"], r => r.Id);

        Assert.True(plan.IsValid);
        Assert.Equal([1, 2, 3], current.Select(r => r.SortOrder));

        var changed = plan.Apply();
        Assert.Equal([3, 2, 1], current.Select(r => r.SortOrder));
        // b 落回原位，不该出现在写库清单里。
        Assert.Equal(["a", "c"], changed.Select(r => r.Id).Order());
    }

    [Fact]
    public void Plan_OrderUnchanged_ProducesNoChanges()
    {
        var current = Sequence("a", "b", "c");

        var plan = SortOrderPlanner.Plan(current, ["a", "b", "c"], r => r.Id);

        Assert.True(plan.IsValid);
        Assert.Empty(plan.Changes);
        Assert.Empty(plan.Apply());
    }

    /// <summary>序号有空洞（10/20/30 这类留空隙的老数据）时，重排把整段规整成连续序号。</summary>
    [Fact]
    public void Plan_SparseSortOrders_AreCompactedToContiguousSequence()
    {
        var current = new List<Row>
        {
            new() { Id = "a", SortOrder = 10 },
            new() { Id = "b", SortOrder = 20 },
            new() { Id = "c", SortOrder = 30 },
        };

        var plan = SortOrderPlanner.Plan(current, ["b", "a", "c"], r => r.Id);

        Assert.True(plan.IsValid);
        plan.Apply();
        Assert.Equal([1, 2, 3], current.OrderBy(r => r.SortOrder).Select(r => r.SortOrder));
        Assert.Equal(1, current.Single(r => r.Id == "b").SortOrder);
    }

    [Fact]
    public void Plan_CustomStartAt_OffsetsTheWholeSequence()
    {
        var current = Sequence("a", "b");

        var plan = SortOrderPlanner.Plan(current, ["b", "a"], r => r.Id, startAt: 100);

        Assert.True(plan.IsValid);
        plan.Apply();
        Assert.Equal(100, current.Single(r => r.Id == "b").SortOrder);
        Assert.Equal(101, current.Single(r => r.Id == "a").SortOrder);
    }

    [Fact]
    public void Plan_EmptyRequest_IsRejected()
    {
        var plan = SortOrderPlanner.Plan(Sequence("a"), Array.Empty<string>(), r => r.Id);

        Assert.False(plan.IsValid);
        Assert.Equal(400, plan.ErrorStatusCode);
        Assert.Empty(plan.Ordered);
    }

    [Fact]
    public void Plan_DuplicateIdInRequest_IsRejected()
    {
        var plan = SortOrderPlanner.Plan(Sequence("a", "b"), ["a", "a"], r => r.Id);

        Assert.False(plan.IsValid);
        Assert.Equal(400, plan.ErrorStatusCode);
    }

    [Fact]
    public void Plan_IdOutsideTheGroup_IsRejectedAsNotFound()
    {
        var plan = SortOrderPlanner.Plan(Sequence("a", "b"), ["a", "zzz"], r => r.Id);

        Assert.False(plan.IsValid);
        Assert.Equal(404, plan.ErrorStatusCode);
    }

    [Fact]
    public void Plan_DuplicateKeyInCurrentOrder_IsRejected()
    {
        var current = new List<Row>
        {
            new() { Id = "a", SortOrder = 1 },
            new() { Id = "a", SortOrder = 2 },
        };

        var plan = SortOrderPlanner.Plan(current, ["a"], r => r.Id);

        Assert.False(plan.IsValid);
        Assert.Equal(400, plan.ErrorStatusCode);
    }

    [Fact]
    public void Plan_SingleRecordGroup_Works()
    {
        var current = Sequence("only");

        var plan = SortOrderPlanner.Plan(current, ["only"], r => r.Id);

        Assert.True(plan.IsValid);
        Assert.Empty(plan.Changes);
    }
}
