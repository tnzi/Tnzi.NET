using Tnzi.Domain.Entities;

namespace Tnzi.Tests.Domain;

/// <summary>
/// <see cref="ChildCollectionSync.ReplaceChildren{TParent,TChild,TKey}"/> 行为测试：
/// 软删路径（保留在集合、标 IsDeleted）、物理删路径（移出集合）、新增、复活、保留匹配项。
/// </summary>
public class ChildCollectionSyncTests
{
    private sealed class Parent
    {
        public List<SoftChild> SoftChildren { get; } = [];
        public List<PlainChild> PlainChildren { get; } = [];
    }

    private sealed class SoftChild : ISoftDelete
    {
        public int Key { get; init; }
        public string Name { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }
    }

    private sealed class PlainChild
    {
        public int Key { get; init; }
    }

    [Fact]
    public void ReplaceChildren_RemovedSoftDeleteChild_ShouldBeMarkedNotRemoved()
    {
        var parent = new Parent();
        parent.SoftChildren.AddRange([
            new SoftChild { Key = 1 }, new SoftChild { Key = 2 }, new SoftChild { Key = 3 }
        ]);

        var result = ChildCollectionSync.ReplaceChildren(
            parent, p => p.SoftChildren,
            [new SoftChild { Key = 1 }, new SoftChild { Key = 2 }],
            c => c.Key);

        Assert.Equal(1, result.SoftDeleted);
        Assert.Equal(0, result.HardRemoved);
        // The removed child stays in the collection (so EF UPDATEs IsDeleted=true), not dropped.
        Assert.Equal(3, parent.SoftChildren.Count);
        Assert.True(parent.SoftChildren.Single(c => c.Key == 3).IsDeleted);
        Assert.False(parent.SoftChildren.Single(c => c.Key == 1).IsDeleted);
    }

    [Fact]
    public void ReplaceChildren_RemovedPlainChild_ShouldBeRemovedFromCollection()
    {
        var parent = new Parent();
        parent.PlainChildren.AddRange([new PlainChild { Key = 1 }, new PlainChild { Key = 2 }, new PlainChild { Key = 3 }]);

        var result = ChildCollectionSync.ReplaceChildren(
            parent, p => p.PlainChildren,
            [new PlainChild { Key = 1 }],
            c => c.Key);

        Assert.Equal(2, result.HardRemoved);
        Assert.Equal(0, result.SoftDeleted);
        Assert.Equal([1], parent.PlainChildren.Select(c => c.Key));
    }

    [Fact]
    public void ReplaceChildren_NewItems_ShouldBeAdded()
    {
        var parent = new Parent();
        parent.PlainChildren.Add(new PlainChild { Key = 1 });

        var result = ChildCollectionSync.ReplaceChildren(
            parent, p => p.PlainChildren,
            [new PlainChild { Key = 1 }, new PlainChild { Key = 2 }, new PlainChild { Key = 0 }],
            c => c.Key);

        // Key=2 (new) and Key=0 (default → treated as new insert) both added.
        Assert.Equal(2, result.Added);
        Assert.Equal(3, parent.PlainChildren.Count);
    }

    [Fact]
    public void ReplaceChildren_SoftDeletedItemBackInTarget_ShouldRevive()
    {
        var parent = new Parent();
        parent.SoftChildren.Add(new SoftChild { Key = 1, IsDeleted = true });

        var result = ChildCollectionSync.ReplaceChildren(
            parent, p => p.SoftChildren,
            [new SoftChild { Key = 1 }],
            c => c.Key);

        Assert.Equal(1, result.Revived);
        Assert.False(parent.SoftChildren.Single().IsDeleted);
        Assert.Equal(0, result.Added);
    }

    [Fact]
    public void ReplaceChildren_MatchedItem_ShouldNotDuplicateOrMutateName()
    {
        var parent = new Parent();
        parent.SoftChildren.Add(new SoftChild { Key = 1, Name = "original" });

        var result = ChildCollectionSync.ReplaceChildren(
            parent, p => p.SoftChildren,
            [new SoftChild { Key = 1, Name = "incoming" }],
            c => c.Key);

        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.SoftDeleted);
        Assert.Single(parent.SoftChildren);
        // Scalar values of matched items are intentionally left untouched.
        Assert.Equal("original", parent.SoftChildren.Single().Name);
    }

    [Fact]
    public void ReplaceChildren_MixedAddRemoveKeep_ShouldReportAllCounts()
    {
        var parent = new Parent();
        parent.SoftChildren.AddRange([
            new SoftChild { Key = 1 }, new SoftChild { Key = 2 }, new SoftChild { Key = 3 }
        ]);

        var result = ChildCollectionSync.ReplaceChildren(
            parent, p => p.SoftChildren,
            [new SoftChild { Key = 2 }, new SoftChild { Key = 4 }, new SoftChild { Key = 0 }],
            c => c.Key);

        Assert.Equal(2, result.Added);       // Key=4 + default-key
        Assert.Equal(2, result.SoftDeleted); // Key=1, Key=3
        Assert.Equal(0, result.HardRemoved);
    }
}
