namespace Tnzi.AI.Tests;

/// <summary>
/// IWebSearchProvider + WebSearchTools 单元测试
/// </summary>
public class WebSearchProviderTests
{

    #region WebSearchTools

    [Fact]
    public async Task WebSearchTools_NoProvider_ReturnsError()
    {
        var tools = new WebSearchTools(Mock.Of<ILogger<WebSearchTools>>());

        var result = await tools.SearchAsync("test query");

        // result 是 anonymous object，转 JSON 检查
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("not available");
    }

    [Fact]
    public async Task WebSearchTools_EmptyQuery_ReturnsError()
    {
        var mockProvider = new Mock<IWebSearchProvider>();
        var tools = new WebSearchTools(Mock.Of<ILogger<WebSearchTools>>(), mockProvider.Object);

        var result = await tools.SearchAsync("  ");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("empty");
    }

    [Fact]
    public async Task WebSearchTools_ValidQuery_DelegatesToProvider()
    {
        var mockProvider = new Mock<IWebSearchProvider>();
        mockProvider.Setup(p => p.SearchAsync("test", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WebSearchResult>
            {
                new() { Title = "Result 1", Url = "https://example.com", Snippet = "A snippet" }
            });

        var tools = new WebSearchTools(Mock.Of<ILogger<WebSearchTools>>(), mockProvider.Object);

        var result = await tools.SearchAsync("test");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("Result 1");
        json.ShouldContain("https://example.com");
        mockProvider.Verify(p => p.SearchAsync("test", 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WebSearchTools_MaxResultsClamped()
    {
        var mockProvider = new Mock<IWebSearchProvider>();
        mockProvider.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WebSearchResult>());

        var tools = new WebSearchTools(Mock.Of<ILogger<WebSearchTools>>(), mockProvider.Object);

        // max_results = 100 should be clamped to 10
        await tools.SearchAsync("test", 100);
        mockProvider.Verify(p => p.SearchAsync("test", 10, It.IsAny<CancellationToken>()), Times.Once);

        // max_results = -5 should be clamped to 1
        await tools.SearchAsync("test", -5);
        mockProvider.Verify(p => p.SearchAsync("test", 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WebSearchTools_ProviderThrows_ReturnsError()
    {
        var mockProvider = new Mock<IWebSearchProvider>();
        mockProvider.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var tools = new WebSearchTools(Mock.Of<ILogger<WebSearchTools>>(), mockProvider.Object);

        var result = await tools.SearchAsync("test");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("Connection refused");
    }

    #endregion

    #region WebSearchResult Model

    [Fact]
    public void WebSearchResult_DefaultValues()
    {
        var result = new WebSearchResult();

        result.Title.ShouldBe(string.Empty);
        result.Url.ShouldBe(string.Empty);
        result.Snippet.ShouldBe(string.Empty);
    }

    #endregion
}
