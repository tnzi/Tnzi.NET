using Tnzi.AI.Tools.Attributes;

namespace Tnzi.AI.Tests;

public class ToolScannerMetadataTests
{
    [AIToolGroup("test")]
    private sealed class MetadataToolProvider : IAIToolProvider
    {
        [AIFunction("metadata_tool", "Tool with deferred metadata",
            SearchHint = "docs search",
            Aliases = "doc_lookup,knowledge_search",
            ShouldDefer = true,
            AlwaysLoad = false,
            Priority = 7,
            ServerHint = "knowledge-base")]
        public string MetadataTool() => "ok";
    }

    [Fact]
    public void ScanAssembly_MapsDeferredMetadata()
    {
        var scanner = new ToolScanner(NullLogger<ToolScanner>.Instance);

        var tool = Assert.Single(scanner.ScanAssembly(typeof(ToolScannerMetadataTests).Assembly)
            .Where(x => x.Name == "metadata_tool"));

        Assert.Equal("docs search", tool.SearchHint);
        Assert.Equal(["doc_lookup", "knowledge_search"], tool.Aliases);
        Assert.True(tool.ShouldDefer);
        Assert.False(tool.AlwaysLoad);
        Assert.Equal(7, tool.Priority);
        Assert.Equal("knowledge-base", tool.ServerHint);
    }
}
