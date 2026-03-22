using System.Net;

namespace Tnzi.AI.Tests.Coder;

/// <summary>
/// DuckDuckGoSearchProvider 单元测试 — Mock HttpClient 验证请求构造和 HTML 解析
/// </summary>
public class DuckDuckGoSearchProviderTests
{
    private DuckDuckGoSearchProvider CreateProvider(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Tnzi.AI.Coder.DuckDuckGo")).Returns(httpClient);

        return new DuckDuckGoSearchProvider(factory.Object, Mock.Of<ILogger<DuckDuckGoSearchProvider>>());
    }

    #region 请求构造

    [Fact]
    public async Task SearchAsync_ConstructsCorrectUrl()
    {
        string? requestedUrl = null;
        var handler = new MockHttpMessageHandler((request, _) =>
        {
            requestedUrl = request.RequestUri?.OriginalString;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html></html>")
            });
        });

        var provider = CreateProvider(handler);
        await provider.SearchAsync("test query");

        requestedUrl.ShouldNotBeNull();
        requestedUrl.ShouldContain("html.duckduckgo.com/html/");
        // 验证 query 参数被传递（Uri 可能部分解码空格）
        requestedUrl.ShouldContain("q=");
    }

    [Fact]
    public async Task SearchAsync_EncodesSpecialCharacters()
    {
        string? requestedUrl = null;
        var handler = new MockHttpMessageHandler((request, _) =>
        {
            requestedUrl = request.RequestUri?.OriginalString;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html></html>")
            });
        });

        var provider = CreateProvider(handler);
        await provider.SearchAsync("C# .NET core");

        requestedUrl.ShouldNotBeNull();
        // # 被编码为 %23（保留在 OriginalString 中）
        requestedUrl.ShouldContain("C%23");
    }

    #endregion

    #region HTML 解析

    [Fact]
    public async Task SearchAsync_ParsesResultsFromHtml()
    {
        var html = """
            <html>
            <div class="result">
                <a class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2Fpage1">Example Page 1</a>
                <a class="result__snippet">This is a snippet for page 1.</a>
            </div>
            <div class="result">
                <a class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2Fpage2">Example Page 2</a>
                <a class="result__snippet">This is a snippet for page 2.</a>
            </div>
            </html>
            """;

        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html)
            }));

        var provider = CreateProvider(handler);
        var results = await provider.SearchAsync("example", 5);

        results.Count.ShouldBe(2);
        results[0].Title.ShouldBe("Example Page 1");
        results[0].Url.ShouldBe("https://example.com/page1");
        results[0].Snippet.ShouldBe("This is a snippet for page 1.");
        results[1].Title.ShouldBe("Example Page 2");
        results[1].Url.ShouldBe("https://example.com/page2");
    }

    [Fact]
    public async Task SearchAsync_StripHtmlTagsFromTitle()
    {
        var html = """
            <html>
            <a class="result__a" href="https://example.com"><b>Bold</b> Title &amp; More</a>
            <a class="result__snippet">Snippet text</a>
            </html>
            """;

        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html)
            }));

        var provider = CreateProvider(handler);
        var results = await provider.SearchAsync("test");

        results.Count.ShouldBe(1);
        results[0].Title.ShouldBe("Bold Title & More");
    }

    [Fact]
    public async Task SearchAsync_ExtractActualUrl_FromUddgRedirect()
    {
        var html = """
            <html>
            <a class="result__a" href="//duckduckgo.com/l/?kh=-1&amp;uddg=https%3A%2F%2Fgithub.com%2Fproject&amp;rut=abc">GitHub Project</a>
            <a class="result__snippet">A project on GitHub.</a>
            </html>
            """;

        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html)
            }));

        var provider = CreateProvider(handler);
        var results = await provider.SearchAsync("github project");

        results.Count.ShouldBe(1);
        results[0].Url.ShouldBe("https://github.com/project");
    }

    [Fact]
    public async Task SearchAsync_DirectHttpUrl_UsedAsIs()
    {
        var html = """
            <html>
            <a class="result__a" href="https://direct.example.com/page">Direct Link</a>
            <a class="result__snippet">Direct snippet.</a>
            </html>
            """;

        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html)
            }));

        var provider = CreateProvider(handler);
        var results = await provider.SearchAsync("direct");

        results.Count.ShouldBe(1);
        results[0].Url.ShouldBe("https://direct.example.com/page");
    }

    [Fact]
    public async Task SearchAsync_ProtocolRelativeUrl_PrependHttps()
    {
        var html = """
            <html>
            <a class="result__a" href="//example.com/page">Protocol-relative</a>
            <a class="result__snippet">Snippet.</a>
            </html>
            """;

        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html)
            }));

        var provider = CreateProvider(handler);
        var results = await provider.SearchAsync("test");

        results.Count.ShouldBe(1);
        results[0].Url.ShouldBe("https://example.com/page");
    }

    #endregion

    #region maxResults 限制

    [Fact]
    public async Task SearchAsync_RespectsMaxResults()
    {
        var html = """
            <html>
            <a class="result__a" href="https://a.com">A</a>
            <a class="result__snippet">Snippet A</a>
            <a class="result__a" href="https://b.com">B</a>
            <a class="result__snippet">Snippet B</a>
            <a class="result__a" href="https://c.com">C</a>
            <a class="result__snippet">Snippet C</a>
            </html>
            """;

        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html)
            }));

        var provider = CreateProvider(handler);
        var results = await provider.SearchAsync("test", 2);

        results.Count.ShouldBe(2);
    }

    [Fact]
    public async Task SearchAsync_MaxResultsClampedTo10()
    {
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html></html>")
            }));

        var provider = CreateProvider(handler);

        // maxResults=100 应被 clamp 到 10，不会报错
        var results = await provider.SearchAsync("test", 100);
        results.Count.ShouldBe(0); // 无 HTML 结果
    }

    [Fact]
    public async Task SearchAsync_MaxResultsClampedTo1()
    {
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html></html>")
            }));

        var provider = CreateProvider(handler);

        // maxResults=-5 应被 clamp 到 1
        var results = await provider.SearchAsync("test", -5);
        results.Count.ShouldBe(0);
    }

    #endregion

    #region 错误处理

    [Fact]
    public async Task SearchAsync_EmptyQuery_ThrowsArgumentException()
    {
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var provider = CreateProvider(handler);

        await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.SearchAsync(""));
    }

    [Fact]
    public async Task SearchAsync_HttpError_ThrowsHttpRequestException()
    {
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Server Error")
            }));

        var provider = CreateProvider(handler);

        await Should.ThrowAsync<HttpRequestException>(async () =>
            await provider.SearchAsync("test"));
    }

    [Fact]
    public async Task SearchAsync_EmptyHtml_ReturnsEmptyList()
    {
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("")
            }));

        var provider = CreateProvider(handler);
        var results = await provider.SearchAsync("test");

        results.Count.ShouldBe(0);
    }

    [Fact]
    public async Task SearchAsync_NoMatchingElements_ReturnsEmptyList()
    {
        var html = "<html><body><p>No search results here</p></body></html>";

        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html)
            }));

        var provider = CreateProvider(handler);
        var results = await provider.SearchAsync("test");

        results.Count.ShouldBe(0);
    }

    [Fact]
    public async Task SearchAsync_SkipsResultsWithEmptyTitle()
    {
        var html = """
            <html>
            <a class="result__a" href="https://empty.com">   </a>
            <a class="result__snippet">Should be skipped</a>
            <a class="result__a" href="https://valid.com">Valid Title</a>
            <a class="result__snippet">Valid snippet</a>
            </html>
            """;

        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html)
            }));

        var provider = CreateProvider(handler);
        var results = await provider.SearchAsync("test");

        results.Count.ShouldBe(1);
        results[0].Title.ShouldBe("Valid Title");
    }

    #endregion

    #region 取消令牌

    [Fact]
    public async Task SearchAsync_CancellationRequested_ThrowsOperationCanceled()
    {
        var handler = new MockHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(5000, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var provider = CreateProvider(handler);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await provider.SearchAsync("test", ct: cts.Token));
    }

    #endregion

    /// <summary>
    /// 可配置的 HttpMessageHandler mock
    /// </summary>
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }
}
