namespace Tnzi.Audit.Tests.Services;

/// <summary>
/// EntityAuditCollector 单元测试 - per-request 实体变更累积与 drain 语义
/// </summary>
public class EntityAuditCollectorTests
{
    private static AuditEntityEntry MakeEntry(string typeName = "Product") => new()
    {
        EntityTypeName = typeName,
        EntityTypeFullName = $"Test.{typeName}",
        OperationType = Tnzi.Audit.Metadata.EntityState.Modified,
        CreationTime = DateTime.UtcNow
    };

    [Fact]
    public void NewCollector_HasNoEntries()
    {
        var collector = new EntityAuditCollector();

        collector.HasEntries.ShouldBeFalse();
        collector.Drain().ShouldBeEmpty();
    }

    [Fact]
    public void AddRange_AccumulatesAcrossCalls()
    {
        var collector = new EntityAuditCollector();

        collector.AddRange([MakeEntry("A")]);
        collector.AddRange([MakeEntry("B"), MakeEntry("C")]);

        collector.HasEntries.ShouldBeTrue();
        var drained = collector.Drain();
        drained.Count.ShouldBe(3);
        drained.Select(e => e.EntityTypeName).ShouldBe(["A", "B", "C"]);
    }

    [Fact]
    public void Drain_EmptiesCollector()
    {
        var collector = new EntityAuditCollector();
        collector.AddRange([MakeEntry()]);

        collector.Drain().Count.ShouldBe(1);

        collector.HasEntries.ShouldBeFalse();
        collector.Drain().ShouldBeEmpty();
    }

    [Fact]
    public void AddRange_AfterDrain_StartsFresh()
    {
        var collector = new EntityAuditCollector();
        collector.AddRange([MakeEntry("A")]);
        var first = collector.Drain();

        collector.AddRange([MakeEntry("B")]);

        // Drain 返回的快照不受后续写入影响
        first.Count.ShouldBe(1);
        first[0].EntityTypeName.ShouldBe("A");
        var second = collector.Drain();
        second.Count.ShouldBe(1);
        second[0].EntityTypeName.ShouldBe("B");
    }
}
