namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// ViewImageMiddleware 单元测试
/// </summary>
public class ViewImageMiddlewareTests
{
    #region Helpers

    private static ViewImageMiddleware CreateMiddleware()
    {
        return new ViewImageMiddleware(Mock.Of<ILogger<ViewImageMiddleware>>());
    }

    private static AiMiddlewareContext CreateContext(
        List<(string MimeType, string Base64)>? viewedImages = null,
        bool alreadyInjected = false)
    {
        var context = new AiMiddlewareContext
        {
            Request = new AgentRunRequest { UserMessage = "Analyze this image" },
            Agent = new AgentResolution
            {
                Agent = null!,
                Provider = "test",
                Model = "test"
            },
            ServiceProvider = new Mock<IServiceProvider>().Object
        };

        if (viewedImages != null)
        {
            context.Properties[ViewImageTool.ViewedImagesKey] = viewedImages;
        }

        if (alreadyInjected)
        {
            context.Properties[ViewImageTool.ViewedImagesInjectedKey] = true;
        }

        return context;
    }

    private static Task<AgentRunResult> SuccessNext(AiMiddlewareContext ctx, CancellationToken ct)
    {
        return Task.FromResult(new AgentRunResult { Response = "ok" });
    }

    #endregion

    #region No viewed images: pass through

    [Fact]
    public async Task NoViewedImages_PassesThrough_NoMessagesAdded()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext();
        var initialMessageCount = context.Messages.Count;

        await middleware.InvokeAsync(context, SuccessNext);

        context.Messages.Count.ShouldBe(initialMessageCount);
    }

    #endregion

    #region With viewed images: injects ImageContent

    [Fact]
    public async Task WithViewedImages_InjectsMultimodalMessage()
    {
        var middleware = CreateMiddleware();
        var images = new List<(string MimeType, string Base64)>
        {
            ("image/png", Convert.ToBase64String(new byte[] { 1, 2, 3 }))
        };
        var context = CreateContext(viewedImages: images);

        await middleware.InvokeAsync(context, SuccessNext);

        // 应该添加了一条消息
        context.Messages.Count.ShouldBe(1);

        var message = context.Messages[0];
        message.Role.ShouldBe(ChatRole.User);

        // 消息应包含 TextContent + ImageContent
        var contents = message.Contents.ToList();
        contents.Count.ShouldBe(2);
        contents[0].ShouldBeOfType<TextContent>();
        contents[1].ShouldBeOfType<DataContent>();

        // 验证 injected 标记
        context.Properties[ViewImageTool.ViewedImagesInjectedKey].ShouldBe(true);
    }

    [Fact]
    public async Task WithMultipleImages_InjectsAllAsImageContent()
    {
        var middleware = CreateMiddleware();
        var images = new List<(string MimeType, string Base64)>
        {
            ("image/png", Convert.ToBase64String(new byte[] { 1, 2, 3 })),
            ("image/jpeg", Convert.ToBase64String(new byte[] { 4, 5, 6 }))
        };
        var context = CreateContext(viewedImages: images);

        await middleware.InvokeAsync(context, SuccessNext);

        context.Messages.Count.ShouldBe(1);

        var contents = context.Messages[0].Contents.ToList();
        // 1 TextContent + 2 ImageContent
        contents.Count.ShouldBe(3);
        contents[0].ShouldBeOfType<TextContent>();
        contents[1].ShouldBeOfType<DataContent>();
        contents[2].ShouldBeOfType<DataContent>();
    }

    #endregion

    #region Dedup: already injected

    [Fact]
    public async Task AlreadyInjected_DoesNotInjectAgain()
    {
        var middleware = CreateMiddleware();
        var images = new List<(string MimeType, string Base64)>
        {
            ("image/png", Convert.ToBase64String(new byte[] { 1, 2, 3 }))
        };
        var context = CreateContext(viewedImages: images, alreadyInjected: true);

        await middleware.InvokeAsync(context, SuccessNext);

        // 不应添加消息
        context.Messages.Count.ShouldBe(0);
    }

    #endregion

}
