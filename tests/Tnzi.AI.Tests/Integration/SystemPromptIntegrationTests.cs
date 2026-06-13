using System.Net;
using System.Net.Sockets;
using System.Text;
using Tnzi.AI.Infrastructure.Network;
using Tnzi.AI.Infrastructure.Readability;

namespace Tnzi.AI.Tests.Integration;

public class SystemPromptIntegrationTests
{
    /// <summary>
    /// 验证 SystemPromptTemplateBuilder 包装的 instructions 可作为 ChatMessage 注入
    /// </summary>
    [Fact]
    public void WrappedInstructions_CanBeUsedAsChatMessage()
    {
        var prompt = SystemPromptTemplateBuilder.WrapInstructions("Always respond in English.");

        var message = new ChatMessage(ChatRole.System, prompt);
        message.Role.ShouldBe(ChatRole.System);
        message.Text.ShouldContain("<instructions>");
        message.Text.ShouldContain("Always respond in English.");
        message.Text.ShouldContain("</instructions>");
    }

    /// <summary>
    /// 验证 SubAgentRegistry 的内置定义可生成委派摘要文本
    /// </summary>
    [Fact]
    public void SubAgentRegistry_CanGenerateDelegationSummary()
    {
        var registry = new SubAgentRegistry();
        var types = registry.GetAll();

        var sb = new StringBuilder();
        sb.AppendLine("Available sub-agent types for delegation:");
        foreach (var t in types)
        {
            sb.AppendLine($"- **{t.Name}**: {t.Description} (max {t.MaxTurns} turns)");
        }

        var summary = sb.ToString();
        summary.ShouldContain("general-purpose");
        summary.ShouldContain("bash");
        summary.ShouldContain("researcher");
    }

    /// <summary>
    /// 验证 PortAllocator 分配的端口可以真正绑定
    /// </summary>
    [Fact]
    public void PortAllocator_AllocatedPort_IsBindable()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new PortAllocatorOptions { StartPort = 19500, MaxRange = 50 });
        var logger = NullLoggerFactory.Instance.CreateLogger<PortAllocator>();
        using var allocator = new PortAllocator(options, logger);

        using var reservation = allocator.Allocate();

        using var listener = new TcpListener(IPAddress.Any, reservation.Port);
        listener.Start();
        ((IPEndPoint)listener.LocalEndpoint).Port.ShouldBe(reservation.Port);
        listener.Stop();
    }

    /// <summary>
    /// 验证 ReadabilityExtractor 对真实 HTML 文章的端到端提取
    /// </summary>
    [Fact]
    public async Task ReadabilityExtractor_RealArticleHtml_ExtractsContent()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger<SmartReaderExtractor>();
        var extractor = new SmartReaderExtractor(logger);

        var html = """
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8">
                <title>New .NET 10 Features Announced at Build 2026</title>
                <meta name="author" content="Jane Developer">
            </head>
            <body>
                <header>
                    <nav><a href="/">Home</a><a href="/blog">Blog</a></nav>
                </header>
                <main>
                    <article>
                        <h1>New .NET 10 Features Announced at Build 2026</h1>
                        <time datetime="2026-03-15">March 15, 2026</time>
                        <p>Microsoft has announced several exciting new features coming to .NET 10
                        at the Build 2026 conference. The release focuses on developer productivity
                        and performance improvements across the entire stack.</p>
                        <p>Among the highlights are native AOT improvements that reduce startup time
                        by up to 40% compared to .NET 9. The new compilation pipeline uses advanced
                        tree-shaking and dead code elimination techniques.</p>
                        <p>Entity Framework Core 10 introduces a new query compilation cache that
                        dramatically reduces first-query latency in high-traffic applications.
                        The cache uses a novel hash-based approach that avoids the overhead of
                        expression tree comparison.</p>
                        <p>ASP.NET Core 10 brings a redesigned middleware pipeline with improved
                        streaming support and native WebTransport integration. The new pipeline
                        reduces per-request overhead by approximately 15%.</p>
                    </article>
                </main>
                <aside>
                    <h3>Related Articles</h3>
                    <ul><li><a href="/post/2">C# 13 Features</a></li></ul>
                </aside>
                <footer><p>Copyright 2026 TechBlog Inc.</p></footer>
            </body>
            </html>
            """;

        var result = await extractor.ExtractAsync(html);

        result.ShouldNotBeNull();
        result.TextContent.ShouldNotBeNullOrWhiteSpace();
        result.TextContent.ShouldContain(".NET 10");
        result.TextContent.ShouldContain("Entity Framework");
    }
}
