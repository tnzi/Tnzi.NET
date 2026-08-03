
namespace Tnzi.AI.Tests;

public class FileConfigChangeDetectorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileConfigChangeDetector _detector;

    public FileConfigChangeDetectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _detector = new FileConfigChangeDetector(NullLogger<FileConfigChangeDetector>.Instance);
    }

    public void Dispose()
    {
        _detector.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task WatchFile_NoChange_DoesNotFire()
    {
        var file = Path.Combine(_tempDir, "test.json");
        await File.WriteAllTextAsync(file, "{}");

        var fired = false;
        _detector.Watch(file, () => { fired = true; return Task.CompletedTask; });

        await _detector.CheckForChangesAsync();
        Assert.False(fired);
    }

    [Fact]
    public async Task WatchFile_FileModified_FiresCallback()
    {
        var file = Path.Combine(_tempDir, "test.json");
        await File.WriteAllTextAsync(file, "{}");

        // 建立基线
        var fired = false;
        _detector.Watch(file, () => { fired = true; return Task.CompletedTask; });
        await _detector.CheckForChangesAsync(); // 第一次检查，不应触发

        Assert.False(fired);

        // 修改文件（确保 mtime 变化）
        await Task.Delay(100);
        await File.WriteAllTextAsync(file, "{\"changed\": true}");

        await _detector.CheckForChangesAsync(); // 第二次检查，mtime 变化，应触发

        Assert.True(fired);
    }

    [Fact]
    public async Task WatchDirectory_NewFileAdded_FiresCallback()
    {
        var subDir = Path.Combine(_tempDir, "skills");
        Directory.CreateDirectory(subDir);

        var fired = false;
        _detector.WatchDirectory(subDir, "*.md", () => { fired = true; return Task.CompletedTask; });

        await _detector.CheckForChangesAsync(); // 建立基线

        // 添加新文件
        await File.WriteAllTextAsync(Path.Combine(subDir, "SKILL.md"), "# New Skill");

        await _detector.CheckForChangesAsync();
        Assert.True(fired);
    }

    [Fact]
    public void Watch_NonExistentFile_DoesNotThrow()
    {
        var file = Path.Combine(_tempDir, "nonexistent.json");
        _detector.Watch(file, () => Task.CompletedTask);
        // 不应抛出异常
    }

    [Fact]
    public async Task Unwatch_RemovesTracking()
    {
        var file = Path.Combine(_tempDir, "test.json");
        await File.WriteAllTextAsync(file, "{}");

        var fired = false;
        _detector.Watch(file, () => { fired = true; return Task.CompletedTask; });
        _detector.Unwatch(file);

        await File.WriteAllTextAsync(file, "{\"changed\": true}");
        await _detector.CheckForChangesAsync();

        Assert.False(fired);
    }

    [Fact]
    public async Task AtomicWrite_UseTempFileRename()
    {
        var file = Path.Combine(_tempDir, "atomic.json");
        await FileConfigChangeDetector.WriteAtomicAsync(file, "{\"data\": 42}");

        Assert.True(File.Exists(file));
        var content = await File.ReadAllTextAsync(file);
        Assert.Equal("{\"data\": 42}", content);
    }
}
