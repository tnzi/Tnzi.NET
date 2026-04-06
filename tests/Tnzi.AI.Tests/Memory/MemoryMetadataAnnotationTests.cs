namespace Tnzi.AI.Tests.Memory;

public class MemoryMetadataAnnotationTests
{
    [Fact]
    public void FormatMemoryEntry_RecentEntry_NoWarning()
    {
        var entry = new MemoryEntry
        {
            Content = "Use PostgreSQL", Category = "fact",
            Importance = 0.9, CreationTime = DateTime.UtcNow.AddHours(-2)
        };
        var formatted = MemoryContextProvider.FormatMemoryEntry(entry);
        formatted.ShouldContain("[fact, 0 days ago, importance: 0.9]");
        formatted.ShouldContain("Use PostgreSQL");
        formatted.ShouldNotContain("verify");
    }

    [Fact]
    public void FormatMemoryEntry_OldEntry_HasFreshnessWarning()
    {
        var entry = new MemoryEntry
        {
            Content = "Auth uses JWT", Category = "fact",
            Importance = 0.8, CreationTime = DateTime.UtcNow.AddDays(-47)
        };
        var formatted = MemoryContextProvider.FormatMemoryEntry(entry);
        formatted.ShouldContain("[fact, 47 days ago, importance: 0.8]");
        formatted.ShouldContain("verify against current state");
        formatted.ShouldNotContain("\u26a0"); // NO emoji
    }

    [Fact]
    public void FormatMemoryEntry_NullCategory_UsesGeneral()
    {
        var entry = new MemoryEntry
        {
            Content = "Some fact", Category = null,
            Importance = 0.5, CreationTime = DateTime.UtcNow
        };
        var formatted = MemoryContextProvider.FormatMemoryEntry(entry);
        formatted.ShouldContain("[general,");
    }

    [Fact]
    public void FormatMemoryEntry_ZeroImportance_FormatsCorrectly()
    {
        var entry = new MemoryEntry
        {
            Content = "Low importance", Category = "note",
            Importance = 0.0, CreationTime = DateTime.UtcNow.AddDays(-1)
        };
        var formatted = MemoryContextProvider.FormatMemoryEntry(entry);
        formatted.ShouldContain("importance: 0.0");
    }
}
