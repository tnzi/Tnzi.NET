namespace Tnzi.AI.Tests;

public class TextSearchProviderTests
{
    [Fact]
    public async Task GetContextAsync_BeforeAIInvoke_InjectsSystemMessage()
    {
        var mockSearch = new Mock<ITextSearchService>();
        mockSearch.Setup(s => s.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TextSearchResult>
            {
                new() { Text = "Relevant document content", SourceName = "doc-1" }
            });

        var provider = new TextSearchProvider(
            mockSearch.Object,
            new TextSearchOptions { SearchTime = ContextSearchTime.BeforeAIInvoke },
            Mock.Of<ILogger<TextSearchProvider>>());

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Tell me about the document")
        };

        var injection = await provider.GetContextAsync(messages);

        injection.Messages.ShouldNotBeNull();
        injection.Messages!.Count.ShouldBe(1);
        injection.Messages[0].Role.ShouldBe(ChatRole.System);
        injection.Messages[0].Text.ShouldContain("Relevant document content");
    }
}
