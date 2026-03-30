using Tnzi.AI.Infrastructure.Memory;

namespace Tnzi.AI.Tests.Infrastructure;

public class DatabaseMemoryStore_EnhancedTests
{
    [Fact]
    public void NormalizeForDedup_CollapseWhitespace()
    {
        var input = "  Hello   world  \n\n  test  ";
        var normalized = DatabaseMemoryStore.NormalizeForDedup(input);
        Assert.Equal("Hello world test", normalized);
    }

    [Fact]
    public void NormalizeForDedup_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal("", DatabaseMemoryStore.NormalizeForDedup(null));
        Assert.Equal("", DatabaseMemoryStore.NormalizeForDedup(""));
        Assert.Equal("", DatabaseMemoryStore.NormalizeForDedup("   "));
    }

    [Fact]
    public void ScrubUploadMentions_RemovesUploadedFilesBlock()
    {
        var input = "User uploaded a file.\n<uploaded_files>\n  <file name=\"test.pdf\" />\n</uploaded_files>\nPlease analyze.";
        var scrubbed = DatabaseMemoryStore.ScrubUploadMentions(input);
        Assert.DoesNotContain("<uploaded_files>", scrubbed);
        Assert.DoesNotContain("</uploaded_files>", scrubbed);
        Assert.Contains("User uploaded a file.", scrubbed);
        Assert.Contains("Please analyze.", scrubbed);
    }

    [Fact]
    public void ScrubUploadMentions_NoUploadBlock_ReturnsUnchanged()
    {
        var input = "Normal text without uploads";
        var scrubbed = DatabaseMemoryStore.ScrubUploadMentions(input);
        Assert.Equal(input, scrubbed);
    }

    [Fact]
    public void ScrubUploadMentions_MultipleBlocks_RemovesAll()
    {
        var input = "Before <uploaded_files>\nfoo\n</uploaded_files> middle <uploaded_files>\nbar\n</uploaded_files> after";
        var scrubbed = DatabaseMemoryStore.ScrubUploadMentions(input);
        Assert.DoesNotContain("<uploaded_files>", scrubbed);
        Assert.Contains("Before", scrubbed);
        Assert.Contains("after", scrubbed);
    }

    [Fact]
    public void IsDuplicate_SameContentDifferentWhitespace_ReturnsTrue()
    {
        Assert.True(DatabaseMemoryStore.IsDuplicate(
            "User prefers dark mode",
            "User  prefers   dark   mode"));
    }

    [Fact]
    public void IsDuplicate_DifferentContent_ReturnsFalse()
    {
        Assert.False(DatabaseMemoryStore.IsDuplicate(
            "User prefers dark mode",
            "User prefers light mode"));
    }

    [Fact]
    public void PruneByConfidence_RemovesLowConfidence()
    {
        var entries = new List<(string Content, double Confidence)>
        {
            ("High confidence fact", 0.9),
            ("Medium confidence fact", 0.5),
            ("Low confidence fact", 0.1),
            ("Very low confidence fact", 0.05)
        };

        var pruned = DatabaseMemoryStore.PruneByConfidence(entries, maxFacts: 10, confidenceThreshold: 0.2);
        Assert.Equal(2, pruned.Count);
        Assert.Contains(pruned, e => e.Content == "High confidence fact");
        Assert.Contains(pruned, e => e.Content == "Medium confidence fact");
    }

    [Fact]
    public void PruneByConfidence_TruncatesAtMaxFacts()
    {
        var entries = new List<(string Content, double Confidence)>
        {
            ("Fact 1", 0.9),
            ("Fact 2", 0.8),
            ("Fact 3", 0.7),
            ("Fact 4", 0.6)
        };

        var pruned = DatabaseMemoryStore.PruneByConfidence(entries, maxFacts: 2, confidenceThreshold: 0.0);
        Assert.Equal(2, pruned.Count);
        Assert.Equal("Fact 1", pruned[0].Content);
    }
}
