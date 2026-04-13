using Tnzi.AI.Infrastructure.ContextProviders;
using Microsoft.Extensions.Hosting;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// Tests that FileSystemSkillStore invalidates its cache when SkillsOptions change via IOptionsMonitor.
/// </summary>
public class FileSystemSkillStoreCacheTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemSkillStoreCacheTests()
    {
        var baseTempDir = Path.Combine(AppContext.BaseDirectory, ".tmp", "fsstore-cache");
        Directory.CreateDirectory(baseTempDir);
        _tempDir = Path.Combine(baseTempDir, $"tnzi-cache-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // -------------------------------------------------------------------------
    // TTL-based caching (baseline)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_ReturnsCachedResult_WhenTtlNotExpired()
    {
        CreateSkillFile("skill-a", """
            ---
            name: Skill A
            description: First skill.
            ---
            """);

        using var store = CreateStore(ttl: TimeSpan.FromMinutes(5));

        var first = await store.GetAllAsync();
        first.Count.ShouldBe(1);

        // Add a new skill file — TTL has not expired
        CreateSkillFile("skill-b", """
            ---
            name: Skill B
            description: Added after first load.
            ---
            """);

        var second = await store.GetAllAsync();
        second.Count.ShouldBe(1); // still cached — new file not visible
        ReferenceEquals(first, second).ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // IOptionsMonitor-triggered invalidation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_ReloadsFromDisk_AfterOptionsChangeTriggersInvalidation()
    {
        CreateSkillFile("skill-a", """
            ---
            name: Skill A
            description: First skill.
            ---
            """);

        var monitor = new TestOptionsMonitor(BuildAiOptions(ttl: TimeSpan.FromMinutes(5)));
        using var store = CreateStoreWithMonitor(monitor);

        var first = await store.GetAllAsync();
        first.Count.ShouldBe(1);

        // Add a new skill file while cache is still live
        CreateSkillFile("skill-b", """
            ---
            name: Skill B
            description: Added after cache.
            ---
            """);

        // Trigger options change — cache should be invalidated
        monitor.TriggerChange();

        var second = await store.GetAllAsync();
        second.Count.ShouldBe(2); // re-scanned — both files visible
        second.ShouldContain(s => s.Name == "Skill A");
        second.ShouldContain(s => s.Name == "Skill B");
    }

    [Fact]
    public async Task GetAllAsync_ReturnsSameReference_WhenNoOptionsChange()
    {
        CreateSkillFile("skill-x", """
            ---
            name: Skill X
            description: Cached skill.
            ---
            """);

        var monitor = new TestOptionsMonitor(BuildAiOptions(ttl: TimeSpan.FromMinutes(10)));
        using var store = CreateStoreWithMonitor(monitor);

        var first = await store.GetAllAsync();
        var second = await store.GetAllAsync();

        // No change triggered — same cached list
        ReferenceEquals(first, second).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllAsync_InvalidatesRepeatedly_AfterEachOptionsChange()
    {
        CreateSkillFile("skill-1", """
            ---
            name: Skill 1
            description: First.
            ---
            """);

        var monitor = new TestOptionsMonitor(BuildAiOptions(ttl: TimeSpan.FromMinutes(5)));
        using var store = CreateStoreWithMonitor(monitor);

        var first = await store.GetAllAsync();
        first.Count.ShouldBe(1);

        // First change: add skill 2
        CreateSkillFile("skill-2", """
            ---
            name: Skill 2
            description: Second.
            ---
            """);
        monitor.TriggerChange();

        var second = await store.GetAllAsync();
        second.Count.ShouldBe(2);

        // Second change: add skill 3
        CreateSkillFile("skill-3", """
            ---
            name: Skill 3
            description: Third.
            ---
            """);
        monitor.TriggerChange();

        var third = await store.GetAllAsync();
        third.Count.ShouldBe(3);
    }

    // -------------------------------------------------------------------------
    // Dispose
    // -------------------------------------------------------------------------

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var monitor = new TestOptionsMonitor(BuildAiOptions());
        var store = CreateStoreWithMonitor(monitor);
        var ex = Record.Exception(() => store.Dispose());
        ex.ShouldBeNull();
    }

    [Fact]
    public async Task GetAllAsync_WorksNormally_WithoutOptionsMonitor()
    {
        CreateSkillFile("standalone-skill", """
            ---
            name: Standalone Skill
            description: No monitor.
            ---
            """);

        // CreateStore uses IOptions only — no monitor
        using var store = CreateStore(ttl: TimeSpan.FromMinutes(5));
        var skills = await store.GetAllAsync();
        skills.Count.ShouldBe(1);
        skills[0].Name.ShouldBe("Standalone Skill");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void CreateSkillFile(string folderName, string content)
    {
        var dir = Path.Combine(_tempDir, folderName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), content);
    }

    private AIOptions BuildAiOptions(TimeSpan? ttl = null) => new()
    {
        ContextProviders = new ContextProvidersOptions
        {
            Skills = new SkillsOptions
            {
                Paths = [_tempDir],
                LoadBuiltIn = false,
                CacheTtl = ttl ?? TimeSpan.FromMinutes(5)
            }
        }
    };

    private FileSystemSkillStore CreateStore(TimeSpan? ttl = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(BuildAiOptions(ttl));
        var logger = Mock.Of<ILogger<FileSystemSkillStore>>();
        return new FileSystemSkillStore(logger, options);
    }

    private FileSystemSkillStore CreateStoreWithMonitor(TestOptionsMonitor monitor)
    {
        var options = Microsoft.Extensions.Options.Options.Create(monitor.CurrentValue);
        var logger = Mock.Of<ILogger<FileSystemSkillStore>>();
        return new FileSystemSkillStore(logger, options, optionsMonitor: monitor);
    }

    // -------------------------------------------------------------------------
    // Test helper: a minimal IOptionsMonitor<AIOptions> that supports OnChange
    // -------------------------------------------------------------------------

    private sealed class TestOptionsMonitor : IOptionsMonitor<AIOptions>
    {
        private readonly List<Action<AIOptions, string?>> _listeners = [];

        public TestOptionsMonitor(AIOptions value)
        {
            CurrentValue = value;
        }

        public AIOptions CurrentValue { get; }

        public AIOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AIOptions, string?> listener)
        {
            _listeners.Add(listener);
            return new Unsubscriber(_listeners, listener);
        }

        public void TriggerChange() =>
            _listeners.ForEach(l => l(CurrentValue, null));

        private sealed class Unsubscriber : IDisposable
        {
            private readonly List<Action<AIOptions, string?>> _list;
            private readonly Action<AIOptions, string?> _item;

            public Unsubscriber(List<Action<AIOptions, string?>> list, Action<AIOptions, string?> item)
            {
                _list = list;
                _item = item;
            }

            public void Dispose() => _list.Remove(_item);
        }
    }
}
