using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.AI.Channels.Store;

namespace Tnzi.Tests.AI.Channels;

public class FileChannelThreadStoreTests : IDisposable
{
    private readonly string _tempFile;
    private readonly FileChannelThreadStore _store;

    public FileChannelThreadStoreTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"tnzi-channel-test-{Guid.NewGuid():N}.json");
        var logger = NullLogger<FileChannelThreadStore>.Instance;
        _store = new FileChannelThreadStore(logger, _tempFile);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    [Fact]
    public async Task GetThreadId_NonExistent_ReturnsNull()
    {
        var result = await _store.GetThreadIdAsync("telegram", "123");
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAndGet_BasicMapping_RoundTrips()
    {
        var threadId = Guid.NewGuid();
        await _store.SetThreadIdAsync("telegram", "123", threadId);

        var result = await _store.GetThreadIdAsync("telegram", "123");
        Assert.Equal(threadId, result);
    }

    [Fact]
    public async Task SetAndGet_WithTopicId_RoundTrips()
    {
        var threadId = Guid.NewGuid();
        await _store.SetThreadIdAsync("telegram", "group1", threadId, topicId: "topic42");

        // Without topicId — should not find
        var withoutTopic = await _store.GetThreadIdAsync("telegram", "group1");
        Assert.Null(withoutTopic);

        // With topicId — should find
        var withTopic = await _store.GetThreadIdAsync("telegram", "group1", topicId: "topic42");
        Assert.Equal(threadId, withTopic);
    }

    [Fact]
    public async Task Remove_ExistingMapping_Removes()
    {
        var threadId = Guid.NewGuid();
        await _store.SetThreadIdAsync("telegram", "123", threadId);
        await _store.RemoveAsync("telegram", "123");

        var result = await _store.GetThreadIdAsync("telegram", "123");
        Assert.Null(result);
    }

    [Fact]
    public async Task Set_OverwritesExisting_ReturnsNewThreadId()
    {
        var threadId1 = Guid.NewGuid();
        var threadId2 = Guid.NewGuid();
        await _store.SetThreadIdAsync("telegram", "123", threadId1);
        await _store.SetThreadIdAsync("telegram", "123", threadId2);

        var result = await _store.GetThreadIdAsync("telegram", "123");
        Assert.Equal(threadId2, result);
    }

    [Fact]
    public async Task AtomicWrite_FilePersistedToDisk()
    {
        var threadId = Guid.NewGuid();
        await _store.SetThreadIdAsync("telegram", "123", threadId);

        Assert.True(File.Exists(_tempFile));
        var json = await File.ReadAllTextAsync(_tempFile);
        Assert.Contains(threadId.ToString(), json);
    }
}
