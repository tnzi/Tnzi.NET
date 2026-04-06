using Tnzi.AI.Infrastructure.Memory;
using Tnzi.AI.Memory;

namespace Tnzi.AI.Tests.Memory;

public class MemoryConsolidationJobTests
{
    private const string TestScope = "test-scope";

    private static MemoryOptions CreateDefaultOptions() => new()
    {
        MaxConsolidationCallsPerPersist = 5,
        ConsolidateSearchTopK = 3
    };

    private static MemoryConsolidationJob CreateJob(
        Mock<IMemoryStore>? memoryStore = null,
        Mock<IMemoryConsolidator>? consolidator = null,
        MemoryOptions? options = null)
    {
        return new MemoryConsolidationJob(
            (memoryStore ?? new Mock<IMemoryStore>()).Object,
            NullLogger<MemoryConsolidationJob>.Instance,
            options ?? CreateDefaultOptions(),
            consolidator?.Object);
    }

    [Fact]
    public async Task TryConsolidateAsync_NullConsolidator_ReturnsFalse()
    {
        var job = CreateJob(consolidator: null);

        var result = await job.TryConsolidateAsync(TestScope);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task TryConsolidateAsync_TooSoon_ReturnsFalse()
    {
        // 先成功运行一次，使 _lastConsolidationTime 被设置
        var memoryStore = new Mock<IMemoryStore>();
        var consolidator = new Mock<IMemoryConsolidator>();

        // 第一次调用: 返回 12 行（满足 >= 10 新条目门控）
        var twelveLines = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"entry {i}"));
        memoryStore.Setup(s => s.ReadAsync(TestScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twelveLines);
        memoryStore.Setup(s => s.SearchAsync(TestScope, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemorySearchResult>());

        var job = CreateJob(memoryStore, consolidator);

        // 第一次应成功
        var firstResult = await job.TryConsolidateAsync(TestScope);
        firstResult.ShouldBeTrue();

        // 第二次应被时间门控拒绝（24h 内）
        var secondResult = await job.TryConsolidateAsync(TestScope);
        secondResult.ShouldBeFalse();
    }

    [Fact]
    public async Task TryConsolidateAsync_NotEnoughNewEntries_ReturnsFalse()
    {
        var memoryStore = new Mock<IMemoryStore>();
        var consolidator = new Mock<IMemoryConsolidator>();

        // 返回 5 行（少于 10 新条目门控）
        var fiveLines = string.Join("\n", Enumerable.Range(1, 5).Select(i => $"entry {i}"));
        memoryStore.Setup(s => s.ReadAsync(TestScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fiveLines);

        var job = CreateJob(memoryStore, consolidator);

        var result = await job.TryConsolidateAsync(TestScope);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task TryConsolidateAsync_GatesMet_ExecutesAndReturnsTrue()
    {
        var memoryStore = new Mock<IMemoryStore>();
        var consolidator = new Mock<IMemoryConsolidator>();

        // 12 行满足条目门控
        var twelveLines = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"entry {i}"));
        memoryStore.Setup(s => s.ReadAsync(TestScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twelveLines);

        // SearchAsync 返回匹配结果
        var targetId = Guid.NewGuid();
        var searchResults = new List<MemorySearchResult>
        {
            new() { Id = targetId, Content = "existing entry", Score = 0.8 }
        };
        memoryStore.Setup(s => s.SearchAsync(TestScope, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        // Consolidator 决定 Update
        consolidator.Setup(c => c.ConsolidateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<MemorySearchResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryConsolidationResult(MemoryAction.Update, "merged content", targetId));

        var job = CreateJob(memoryStore, consolidator);

        var result = await job.TryConsolidateAsync(TestScope);

        result.ShouldBeTrue();
        consolidator.Verify(
            c => c.ConsolidateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<MemorySearchResult>>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        memoryStore.Verify(
            s => s.UpdateEntryAsync(TestScope, targetId, "merged content", It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task TryConsolidateAsync_DeleteAction_CallsDeleteEntry()
    {
        var memoryStore = new Mock<IMemoryStore>();
        var consolidator = new Mock<IMemoryConsolidator>();

        var elevenLines = string.Join("\n", Enumerable.Range(1, 11).Select(i => $"entry {i}"));
        memoryStore.Setup(s => s.ReadAsync(TestScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(elevenLines);

        var targetId = Guid.NewGuid();
        memoryStore.Setup(s => s.SearchAsync(TestScope, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemorySearchResult>
            {
                new() { Id = targetId, Content = "outdated", Score = 0.9 }
            });

        consolidator.Setup(c => c.ConsolidateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<MemorySearchResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryConsolidationResult(MemoryAction.Delete, null, targetId));

        var job = CreateJob(memoryStore, consolidator);

        var result = await job.TryConsolidateAsync(TestScope);

        result.ShouldBeTrue();
        memoryStore.Verify(
            s => s.DeleteEntryAsync(TestScope, targetId, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task TryConsolidateAsync_RespectsMaxConsolidationCalls()
    {
        var memoryStore = new Mock<IMemoryStore>();
        var consolidator = new Mock<IMemoryConsolidator>();
        var options = new MemoryOptions { MaxConsolidationCallsPerPersist = 2, ConsolidateSearchTopK = 3 };

        // 15 行
        var fifteenLines = string.Join("\n", Enumerable.Range(1, 15).Select(i => $"entry {i}"));
        memoryStore.Setup(s => s.ReadAsync(TestScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fifteenLines);

        memoryStore.Setup(s => s.SearchAsync(TestScope, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemorySearchResult>
            {
                new() { Id = Guid.NewGuid(), Content = "existing", Score = 0.5 }
            });

        consolidator.Setup(c => c.ConsolidateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<MemorySearchResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryConsolidationResult(MemoryAction.Noop));

        var job = CreateJob(memoryStore, consolidator, options);

        await job.TryConsolidateAsync(TestScope);

        // 最多 2 次合并调用
        consolidator.Verify(
            c => c.ConsolidateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<MemorySearchResult>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task TryConsolidateAsync_ConsolidatorThrows_ReturnsFalse()
    {
        var memoryStore = new Mock<IMemoryStore>();
        var consolidator = new Mock<IMemoryConsolidator>();

        var twelveLines = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"entry {i}"));
        memoryStore.Setup(s => s.ReadAsync(TestScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twelveLines);

        memoryStore.Setup(s => s.SearchAsync(TestScope, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemorySearchResult>
            {
                new() { Id = Guid.NewGuid(), Content = "existing", Score = 0.5 }
            });

        consolidator.Setup(c => c.ConsolidateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<MemorySearchResult>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM timeout"));

        var job = CreateJob(memoryStore, consolidator);

        var result = await job.TryConsolidateAsync(TestScope);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task ResetGates_AllowsImmediateReconsolidation()
    {
        var memoryStore = new Mock<IMemoryStore>();
        var consolidator = new Mock<IMemoryConsolidator>();

        var twelveLines = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"entry {i}"));
        memoryStore.Setup(s => s.ReadAsync(TestScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twelveLines);
        memoryStore.Setup(s => s.SearchAsync(TestScope, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemorySearchResult>());

        var job = CreateJob(memoryStore, consolidator);

        // 第一次成功
        var first = await job.TryConsolidateAsync(TestScope);
        first.ShouldBeTrue();

        // 第二次被门控
        var second = await job.TryConsolidateAsync(TestScope);
        second.ShouldBeFalse();

        // 重置后可以再次运行
        job.ResetGates();
        var third = await job.TryConsolidateAsync(TestScope);
        third.ShouldBeTrue();
    }
}
