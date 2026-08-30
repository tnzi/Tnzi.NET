namespace Tnzi.EFCore.Tests;

/// <summary>
/// <c>IRepository.ReorderAsync</c> 集成测试（真实仓储 + SQLite）。
/// </summary>
/// <remarks>
/// 纯函数那一层（<c>SortOrderPlannerTests</c>）已经证明了槽位保留的算法本身。
/// 这里要证明的是**接线**：范围谓词真的下到查询里、写回真的落库、
/// 校验失败真的没有写库。前两条只有真实仓储能证明——把谓词删掉，
/// 纯函数测试与任何 mock 测试都照常绿。
/// </remarks>
public class RepositoryReorderTests : EFCoreTestBase
{
    private readonly IRepository<TestOrderedItem, Guid> _repository;

    public RepositoryReorderTests()
    {
        _repository = new EFCoreRepository<TestDbContext, TestOrderedItem, Guid>(DbContext, null, ServiceProvider);
    }

    private async Task<List<TestOrderedItem>> SeedAsync(Guid? groupId, params string[] names)
    {
        var items = names
            .Select((n, i) => new TestOrderedItem
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                Name = n,
                SortOrder = i + 1,
            })
            .ToList();

        await _repository.InsertManyAsync(items);
        await DbContext.SaveChangesAsync();
        return items;
    }

    private async Task<List<string>> ReadOrderAsync(Guid? groupId) =>
        await DbContext.OrderedItems
            .Where(i => i.GroupId == groupId)
            .OrderBy(i => i.SortOrder)
            .Select(i => i.Name)
            .ToListAsync();

    [Fact]
    public async Task ReorderAsync_PersistsTheSubmittedOrder()
    {
        var group = Guid.NewGuid();
        var items = await SeedAsync(group, "a", "b", "c");
        var byName = items.ToDictionary(i => i.Name, i => i.Id);

        var result = await _repository.ReorderAsync(
            [byName["c"], byName["a"], byName["b"]],
            i => i.GroupId == group);

        Assert.True(result.Succeeded);
        await DbContext.SaveChangesAsync();
        Assert.Equal(["c", "a", "b"], await ReadOrderAsync(group));
    }

    /// <summary>
    /// 范围谓词的守卫：删掉 <c>scope</c> 会让两个分组被当成一条序列，
    /// 另一组的记录跟着被重编号。这条测试是那个改动唯一会红的地方。
    /// </summary>
    [Fact]
    public async Task ReorderAsync_LeavesOtherScopesUntouched()
    {
        var groupA = Guid.NewGuid();
        var groupB = Guid.NewGuid();
        var a = await SeedAsync(groupA, "a1", "a2", "a3");
        await SeedAsync(groupB, "b1", "b2", "b3");
        var byName = a.ToDictionary(i => i.Name, i => i.Id);

        var result = await _repository.ReorderAsync(
            [byName["a3"], byName["a2"], byName["a1"]],
            i => i.GroupId == groupA);

        Assert.True(result.Succeeded);
        await DbContext.SaveChangesAsync();

        Assert.Equal(["a3", "a2", "a1"], await ReadOrderAsync(groupA));
        Assert.Equal(["b1", "b2", "b3"], await ReadOrderAsync(groupB));

        // B 组的序号也必须原封不动（不是碰巧顺序一样但号全变了）。
        var bOrders = await DbContext.OrderedItems
            .Where(i => i.GroupId == groupB)
            .OrderBy(i => i.Name)
            .Select(i => i.SortOrder)
            .ToListAsync();
        Assert.Equal([1, 2, 3], bOrders);
    }

    /// <summary>提交的只是范围内的一部分（分页场景）时，未提交的记录留在原位。</summary>
    [Fact]
    public async Task ReorderAsync_PartialSubmission_KeepsUnsubmittedRecordsInPlace()
    {
        var group = Guid.NewGuid();
        var items = await SeedAsync(group, "a", "b", "c", "d");
        var byName = items.ToDictionary(i => i.Name, i => i.Id);

        var result = await _repository.ReorderAsync(
            [byName["c"], byName["a"]],
            i => i.GroupId == group);

        Assert.True(result.Succeeded);
        await DbContext.SaveChangesAsync();
        Assert.Equal(["c", "b", "a", "d"], await ReadOrderAsync(group));
    }

    [Fact]
    public async Task ReorderAsync_NoScope_TreatsTheWholeTableAsOneSequence()
    {
        var items = await SeedAsync(null, "x", "y", "z");
        var byName = items.ToDictionary(i => i.Name, i => i.Id);

        var result = await _repository.ReorderAsync([byName["z"], byName["y"], byName["x"]]);

        Assert.True(result.Succeeded);
        await DbContext.SaveChangesAsync();
        Assert.Equal(["z", "y", "x"], await ReadOrderAsync(null));
    }

    [Fact]
    public async Task ReorderAsync_ReturnsNumberOfRowsActuallyChanged()
    {
        var group = Guid.NewGuid();
        var items = await SeedAsync(group, "a", "b", "c");
        var byName = items.ToDictionary(i => i.Name, i => i.Id);

        // 只换 a 与 b：c 落回自己的位置，不该计入。
        var result = await _repository.ReorderAsync(
            [byName["b"], byName["a"], byName["c"]],
            i => i.GroupId == group);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data);
    }

    [Fact]
    public async Task ReorderAsync_UnchangedOrder_WritesNothing()
    {
        var group = Guid.NewGuid();
        var items = await SeedAsync(group, "a", "b", "c");
        var byName = items.ToDictionary(i => i.Name, i => i.Id);

        var result = await _repository.ReorderAsync(
            [byName["a"], byName["b"], byName["c"]],
            i => i.GroupId == group);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Data);
    }

    /// <summary>范围外的 id 必须被拒绝，而不是被悄悄拉进这一组重排。</summary>
    [Fact]
    public async Task ReorderAsync_IdFromAnotherScope_IsRejectedAndNothingIsWritten()
    {
        var groupA = Guid.NewGuid();
        var groupB = Guid.NewGuid();
        var a = await SeedAsync(groupA, "a1", "a2");
        var b = await SeedAsync(groupB, "b1");

        var result = await _repository.ReorderAsync(
            [a[1].Id, b[0].Id],
            i => i.GroupId == groupA);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);

        await DbContext.SaveChangesAsync();
        Assert.Equal(["a1", "a2"], await ReadOrderAsync(groupA));
        Assert.Equal(["b1"], await ReadOrderAsync(groupB));
    }

    [Fact]
    public async Task ReorderAsync_DuplicateId_IsRejected()
    {
        var group = Guid.NewGuid();
        var items = await SeedAsync(group, "a", "b");

        var result = await _repository.ReorderAsync(
            [items[0].Id, items[0].Id],
            i => i.GroupId == group);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    [Fact]
    public async Task ReorderAsync_EmptyRequest_IsRejected()
    {
        var group = Guid.NewGuid();
        await SeedAsync(group, "a");

        var result = await _repository.ReorderAsync([], i => i.GroupId == group);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }
}
