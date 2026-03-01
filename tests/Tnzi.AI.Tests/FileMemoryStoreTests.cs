using Tnzi.AI.Coder.Options;
using Tnzi.AI.Memory;

namespace Tnzi.AI.Tests;

/// <summary>
/// FileMemoryStore 单元测试 — CRUD + TF-IDF 搜索
/// </summary>
public class FileMemoryStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileMemoryStore _store;

    public FileMemoryStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var options = Microsoft.Extensions.Options.Options.Create(new CoderOptions
        {
            ProjectRoot = _tempDir,
            MemoryDirectory = "memory"
        });

        _store = new FileMemoryStore(options, Mock.Of<ILogger<FileMemoryStore>>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    #region ReadAsync / WriteAsync

    [Fact]
    public async Task ReadAsync_NonExistentScope_ReturnsNull()
    {
        var result = await _store.ReadAsync("nonexistent");
        result.ShouldBeNull();
    }

    [Fact]
    public async Task WriteAsync_ThenRead_RoundTrips()
    {
        await _store.WriteAsync("test-scope", "Hello World");
        var result = await _store.ReadAsync("test-scope");
        result.ShouldBe("Hello World");
    }

    [Fact]
    public async Task WriteAsync_Overwrites_PreviousContent()
    {
        await _store.WriteAsync("overwrite", "First");
        await _store.WriteAsync("overwrite", "Second");
        var result = await _store.ReadAsync("overwrite");
        result.ShouldBe("Second");
    }

    [Fact]
    public async Task WriteAsync_DefaultScope_UsesMEMORYFile()
    {
        await _store.WriteAsync("default", "Default content");
        var memoryFile = Path.Combine(_tempDir, "memory", "MEMORY.md");
        File.Exists(memoryFile).ShouldBeTrue();
    }

    #endregion

    #region AppendAsync

    [Fact]
    public async Task AppendAsync_NewScope_CreatesFile()
    {
        await _store.AppendAsync("new-scope", "First entry");
        var result = await _store.ReadAsync("new-scope");
        result.ShouldBe("First entry");
    }

    [Fact]
    public async Task AppendAsync_ExistingScope_AppendsWithNewline()
    {
        await _store.WriteAsync("append-test", "Line 1");
        await _store.AppendAsync("append-test", "Line 2");
        var result = (await _store.ReadAsync("append-test"))!;
        result.ShouldContain("Line 1");
        result.ShouldContain("Line 2");
    }

    #endregion

    #region ClearAsync

    [Fact]
    public async Task ClearAsync_ExistingScope_RemovesContent()
    {
        await _store.WriteAsync("clear-test", "Some content");
        await _store.ClearAsync("clear-test");
        var result = await _store.ReadAsync("clear-test");
        result.ShouldBeNull();
    }

    [Fact]
    public async Task ClearAsync_NonExistentScope_NoError()
    {
        // Should not throw
        await _store.ClearAsync("nonexistent");
    }

    #endregion

    #region SearchAsync (TF-IDF)

    [Fact]
    public async Task SearchAsync_EmptyScope_ReturnsEmpty()
    {
        var results = await _store.SearchAsync("nonexistent", "query");
        results.Count.ShouldBe(0);
    }

    [Fact]
    public async Task SearchAsync_MatchingKeyword_ReturnsResults()
    {
        var content = """
            ## Architecture
            The system uses a modular architecture with dependency injection.

            ## Testing
            Unit tests cover all core components and integration scenarios.

            ## Deployment
            Docker containers are used for production deployment.
            """;

        await _store.WriteAsync("search-test", content);

        var results = await _store.SearchAsync("search-test", "testing unit");
        results.Count.ShouldBeGreaterThan(0);
        results[0].Content.ShouldContain("test");
    }

    [Fact]
    public async Task SearchAsync_NoMatch_ReturnsEmpty()
    {
        await _store.WriteAsync("search-miss", "Simple text about nothing relevant");
        var results = await _store.SearchAsync("search-miss", "xyzzyplugh");
        results.Count.ShouldBe(0);
    }

    [Fact]
    public async Task SearchAsync_ScoreNormalized_Between0And1()
    {
        var content = """
            ## Section A
            This section discusses machine learning algorithms.

            ## Section B
            Another section about machine learning models and training.
            """;

        await _store.WriteAsync("score-test", content);
        var results = await _store.SearchAsync("score-test", "machine learning");

        foreach (var result in results)
        {
            result.Score.ShouldBeGreaterThanOrEqualTo(0);
            result.Score.ShouldBeLessThanOrEqualTo(1);
        }
    }

    [Fact]
    public async Task SearchAsync_RespectsMaxResults()
    {
        var content = """
            ## Part 1
            First topic about programming.

            ## Part 2
            Second topic about programming.

            ## Part 3
            Third topic about programming.
            """;

        await _store.WriteAsync("max-test", content);
        var results = await _store.SearchAsync("max-test", "programming", 2);
        results.Count.ShouldBeLessThanOrEqualTo(2);
    }

    #endregion

    #region Security (Path Traversal)

    [Fact]
    public async Task ReadAsync_PathTraversal_ThrowsArgumentException()
    {
        await Should.ThrowAsync<ArgumentException>(async () =>
            await _store.ReadAsync("../etc/passwd"));
    }

    [Fact]
    public async Task WriteAsync_PathTraversal_ThrowsArgumentException()
    {
        await Should.ThrowAsync<ArgumentException>(async () =>
            await _store.WriteAsync("..\\windows\\system32", "malicious"));
    }

    [Fact]
    public async Task ReadAsync_SlashInScope_ThrowsArgumentException()
    {
        await Should.ThrowAsync<ArgumentException>(async () =>
            await _store.ReadAsync("some/nested/scope"));
    }

    #endregion
}
