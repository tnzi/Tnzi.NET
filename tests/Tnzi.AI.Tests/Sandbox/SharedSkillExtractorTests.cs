using Tnzi.AI.Sandbox.Services;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// Tests for <see cref="SharedSkillExtractor"/> — the startup-time, one-shot
/// extraction of skill resource files into a single shared directory that
/// every thread's <c>skills/</c> symlinks to (instead of every thread keeping
/// its own copy).
/// </summary>
public class SharedSkillExtractorTests : IAsyncLifetime
{
    private string _dataRoot = null!;

    public Task InitializeAsync()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), $"tnzi-shared-skill-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataRoot);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_dataRoot))
        {
            // Clear read-only attribute so the directory can actually be deleted.
            foreach (var file in Directory.EnumerateFiles(_dataRoot, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { /* ignore */ }
            }
            Directory.Delete(_dataRoot, recursive: true);
        }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ExtractAsync_PopulatesSharedRoot_AndWritesMarker()
    {
        var store = new StubSkillStore(
            ("chart-visualization", new() { ["scripts/generate.js"] = "console.log('chart');" }),
            ("data-analysis", new() { ["scripts/analyze.py"] = "import duckdb" }));

        await SharedSkillExtractor.ExtractAsync(
            store, _dataRoot, NullLogger.Instance);

        var sharedRoot = SharedSkillExtractor.GetSharedRoot(_dataRoot);
        Directory.Exists(sharedRoot).ShouldBeTrue();

        File.Exists(Path.Combine(sharedRoot, SharedSkillExtractor.ExtractedMarker)).ShouldBeTrue();

        var chartScript = Path.Combine(sharedRoot, "chart-visualization", "scripts", "generate.js");
        File.Exists(chartScript).ShouldBeTrue();
        (await File.ReadAllTextAsync(chartScript)).ShouldBe("console.log('chart');");

        var analyzeScript = Path.Combine(sharedRoot, "data-analysis", "scripts", "analyze.py");
        File.Exists(analyzeScript).ShouldBeTrue();
    }

    [Fact]
    public async Task ExtractAsync_SetsReadOnlyOnExtractedFiles()
    {
        var store = new StubSkillStore(
            ("demo", new() { ["scripts/x.sh"] = "#!/bin/bash\necho hi" }));

        await SharedSkillExtractor.ExtractAsync(store, _dataRoot, NullLogger.Instance);

        var scriptPath = Path.Combine(SharedSkillExtractor.GetSharedRoot(_dataRoot), "demo", "scripts", "x.sh");
        var attrs = File.GetAttributes(scriptPath);
        attrs.HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();
    }

    [Fact]
    public async Task ExtractAsync_IdempotentViaMarker()
    {
        var sharedRoot = SharedSkillExtractor.GetSharedRoot(_dataRoot);
        Directory.CreateDirectory(sharedRoot);
        await File.WriteAllTextAsync(
            Path.Combine(sharedRoot, SharedSkillExtractor.ExtractedMarker),
            "previous run");

        // Even with skills that would otherwise be written, the marker should
        // short-circuit the extraction entirely.
        var store = new StubSkillStore(
            ("demo", new() { ["scripts/never.sh"] = "should not exist" }));

        await SharedSkillExtractor.ExtractAsync(store, _dataRoot, NullLogger.Instance);

        File.Exists(Path.Combine(sharedRoot, "demo", "scripts", "never.sh")).ShouldBeFalse();
    }

    [Fact]
    public async Task ExtractAsync_EmptySkillStore_WritesMarkerAndReturns()
    {
        var store = new StubSkillStore();

        await SharedSkillExtractor.ExtractAsync(store, _dataRoot, NullLogger.Instance);

        var marker = Path.Combine(SharedSkillExtractor.GetSharedRoot(_dataRoot), SharedSkillExtractor.ExtractedMarker);
        File.Exists(marker).ShouldBeTrue();
    }

    [Fact]
    public async Task ExtractAsync_SkillWithoutResources_IsSkippedWithoutCreatingDirectory()
    {
        var store = new StubSkillStore(
            ("with-resources", new() { ["scripts/has.sh"] = "yes" }),
            ("no-resources", new())); // empty resources dictionary

        await SharedSkillExtractor.ExtractAsync(store, _dataRoot, NullLogger.Instance);

        var sharedRoot = SharedSkillExtractor.GetSharedRoot(_dataRoot);
        Directory.Exists(Path.Combine(sharedRoot, "with-resources")).ShouldBeTrue();
        Directory.Exists(Path.Combine(sharedRoot, "no-resources")).ShouldBeFalse();
    }

    [Fact]
    public async Task ExtractAsync_NestedRelativePath_PreservedInSharedTree()
    {
        // Many built-in skills have nested resource layouts like
        // _office/scripts/schemas/microsoft/wml-2010.xsd
        var store = new StubSkillStore(
            ("office", new() { ["scripts/schemas/microsoft/wml.xsd"] = "<xsd/>" }));

        await SharedSkillExtractor.ExtractAsync(store, _dataRoot, NullLogger.Instance);

        var nested = Path.Combine(
            SharedSkillExtractor.GetSharedRoot(_dataRoot),
            "office", "scripts", "schemas", "microsoft", "wml.xsd");
        File.Exists(nested).ShouldBeTrue();
    }

    [Fact]
    public void GetSharedRoot_ProducesExpectedShape()
    {
        var root = SharedSkillExtractor.GetSharedRoot("/var/lib/tnzi");
        root.ShouldBe(Path.Combine("/var/lib/tnzi", "_skills"));
    }

    // ------------------------------------------------------------------ //
    // Helpers
    // ------------------------------------------------------------------ //

    private sealed class StubSkillStore : ISkillStore
    {
        private readonly List<SkillDefinition> _skills;

        public StubSkillStore(params (string Slug, Dictionary<string, string> Resources)[] skills)
        {
            _skills = skills
                .Select(s => new SkillDefinition
                {
                    Slug = s.Slug,
                    Name = s.Slug,
                    Resources = s.Resources
                })
                .ToList();
        }

        public Task<List<SkillDefinition>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult(_skills);

        public Task<SkillDefinition?> GetBySlugAsync(string slug, CancellationToken ct = default)
            => Task.FromResult(_skills.FirstOrDefault(s => s.Slug == slug));
    }
}
