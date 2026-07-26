using Tnzi.AI.WebSearch;

namespace Tnzi.AI.Tests.Tools;

/// <summary>
/// WebSearchTools 内置工具功能测试
/// </summary>
public class WebSearchToolsTests
{
    #region SearchAsync - 无 Provider

    [Fact]
    public async Task Search_NoProvider_ReturnsError()
    {
        var tools = new WebSearchTools(NullLogger<WebSearchTools>.Instance, searchProvider: null);

        var result = await tools.SearchAsync("test query");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("error");
        json.ShouldContain("not available");
    }

    #endregion

    #region SearchAsync - 空查询

    [Fact]
    public async Task Search_EmptyQuery_ReturnsError()
    {
        var mockProvider = new Mock<IWebSearchProvider>();
        var tools = new WebSearchTools(NullLogger<WebSearchTools>.Instance, mockProvider.Object);

        var result = await tools.SearchAsync("");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("error");
        json.ShouldContain("empty");
    }

    [Fact]
    public async Task Search_WhitespaceQuery_ReturnsError()
    {
        var mockProvider = new Mock<IWebSearchProvider>();
        var tools = new WebSearchTools(NullLogger<WebSearchTools>.Instance, mockProvider.Object);

        var result = await tools.SearchAsync("   ");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("error");
    }

    #endregion

    #region SearchAsync - 正常搜索

    [Fact]
    public async Task Search_ValidQuery_ReturnsResults()
    {
        var mockProvider = new Mock<IWebSearchProvider>();
        mockProvider.Setup(p => p.SearchAsync("dotnet", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WebSearchResult>
            {
                new() { Title = ".NET", Url = "https://dot.net", Snippet = "Free. Cross-platform." },
                new() { Title = "C#", Url = "https://csharp.net", Snippet = "Language" }
            });

        var tools = new WebSearchTools(NullLogger<WebSearchTools>.Instance, mockProvider.Object);

        var result = await tools.SearchAsync("dotnet");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"count\":2");
        json.ShouldContain(".NET");
        json.ShouldContain("https://dot.net");
    }

    [Fact]
    public async Task Search_MaxResultsClamped_To1_10()
    {
        var mockProvider = new Mock<IWebSearchProvider>();
        mockProvider.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WebSearchResult>());

        var tools = new WebSearchTools(NullLogger<WebSearchTools>.Instance, mockProvider.Object);

        // maxResults=0 应被 clamp 到 1
        await tools.SearchAsync("test", maxResults: 0);
        mockProvider.Verify(p => p.SearchAsync("test", 1, It.IsAny<CancellationToken>()));

        // maxResults=20 应被 clamp 到 10
        await tools.SearchAsync("test", maxResults: 20);
        mockProvider.Verify(p => p.SearchAsync("test", 10, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Search_DefaultMaxResults_Is5()
    {
        var mockProvider = new Mock<IWebSearchProvider>();
        mockProvider.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WebSearchResult>());

        var tools = new WebSearchTools(NullLogger<WebSearchTools>.Instance, mockProvider.Object);

        await tools.SearchAsync("test");

        mockProvider.Verify(p => p.SearchAsync("test", 5, It.IsAny<CancellationToken>()));
    }

    #endregion

    #region SearchAsync - 异常处理

    [Fact]
    public async Task Search_ProviderThrows_ReturnsError()
    {
        var mockProvider = new Mock<IWebSearchProvider>();
        mockProvider.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        var tools = new WebSearchTools(NullLogger<WebSearchTools>.Instance, mockProvider.Object);

        var result = await tools.SearchAsync("test");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("error");
        json.ShouldContain("Network error");
    }

    #endregion
}
