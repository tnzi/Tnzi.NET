namespace Tnzi.Tests.Utilities;

/// <summary>
/// TreeHelper.ToTree 可空父键版测试（null / 悬空父 = 根，节点不丢失，保持输入顺序）
/// </summary>
public class TreeHelperTests
{
    private sealed class Node
    {
        public Guid Id { get; init; }
        public Guid? ParentId { get; init; }
        public string Name { get; init; } = string.Empty;
        public List<Node> Children { get; } = new();
    }

    private static Node N(string name, Guid id, Guid? parentId = null) => new() { Id = id, ParentId = parentId, Name = name };

    private static IList<Node> Build(params Node[] nodes)
        => TreeHelper.ToTree(nodes, n => n.Id, n => n.ParentId, (p, c) => p.Children.Add(c));

    [Fact]
    public void ToTree_NullableKey_BuildsHierarchy()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var roots = Build(
            N("root", rootId),
            N("child", childId, rootId),
            N("grandchild", Guid.NewGuid(), childId));

        Assert.Single(roots);
        Assert.Equal("root", roots[0].Name);
        Assert.Single(roots[0].Children);
        Assert.Equal("child", roots[0].Children[0].Name);
        Assert.Single(roots[0].Children[0].Children);
    }

    [Fact]
    public void ToTree_NullableKey_DanglingParentBecomesRoot()
    {
        var roots = Build(
            N("normal", Guid.NewGuid()),
            N("orphan", Guid.NewGuid(), parentId: Guid.NewGuid()));

        // 悬空引用不丢节点：两条都在根列表
        Assert.Equal(2, roots.Count);
        Assert.Contains(roots, n => n.Name == "orphan");
    }

    [Fact]
    public void ToTree_NullableKey_PreservesInputOrder()
    {
        var parentId = Guid.NewGuid();
        var roots = Build(
            N("b", Guid.NewGuid()),
            N("a", Guid.NewGuid()),
            N("c1", Guid.NewGuid(), parentId),
            N("p", parentId),
            N("c2", Guid.NewGuid(), parentId));

        Assert.Equal(new[] { "b", "a", "p" }, roots.Select(n => n.Name));
        Assert.Equal(new[] { "c1", "c2" }, roots.Single(n => n.Name == "p").Children.Select(n => n.Name));
    }

    [Fact]
    public void ToTree_NullableKey_EmptyInputReturnsEmptyList()
    {
        Assert.Empty(Build());
    }
}
