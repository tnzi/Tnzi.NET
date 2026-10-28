using Tnzi.Exceptions;

namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// <see cref="AgentResolver.BuildChatMessageAsync"/> 把请求的内容块变成模型输入：
/// 每一种块要么变成模型真正看得到的内容，要么以调用方能读懂的错误码拒绝，不能变成 500 或一行占位文本。
/// </summary>
public class AgentResolverContentPartTests
{
    private static AgentResolver CreateResolver() => new(
        Mock.Of<IAgentFactory>(),
        new StaticOptionsMonitor<AIOptions>(new AIOptions()),
        Mock.Of<IRepository<Agent, Guid>>(),
        Mock.Of<IUserToolPermissionResolver>(),
        Mock.Of<IPromptTemplateEngine>(),
        Mock.Of<IAgentVersionRouter>(),
        Mock.Of<IAgentGrantService>(),
        NullLogger<AgentResolver>.Instance);

    private static Task<ChatMessage> BuildAsync(params ContentPartDto[] parts)
        => CreateResolver().BuildChatMessageAsync("what is this", [.. parts], CancellationToken.None);

    /// <summary>
    /// DataContent 的 Uri 构造只收 data URI，http(s) 地址进去必抛 ArgumentException，此前一律以 500 收尾。
    /// </summary>
    [Fact]
    public async Task HttpsImageUrl_BecomesUriContentForTheProviderToFetch()
    {
        var message = await BuildAsync(new ImageContentPartDto { Url = "https://example.com/cat.png", MediaType = "image/png" });

        var image = message.Contents.OfType<UriContent>().ShouldHaveSingleItem();
        image.Uri.ShouldBe(new Uri("https://example.com/cat.png"));
        image.MediaType.ShouldBe("image/png");
    }

    [Fact]
    public async Task HttpsImageUrl_WithoutMediaType_IsStillSentAsAnImage()
    {
        var message = await BuildAsync(new ImageContentPartDto { Url = "https://cdn.example.com/assets/12345" });

        var image = message.Contents.OfType<UriContent>().ShouldHaveSingleItem();
        image.HasTopLevelMediaType("image").ShouldBeTrue();
    }

    [Fact]
    public async Task DataUriImage_StaysInlineDataContent()
    {
        var dataUri = "data:image/png;base64," + Convert.ToBase64String([1, 2, 3]);

        var message = await BuildAsync(new ImageContentPartDto { Url = dataUri });

        var image = message.Contents.OfType<DataContent>().ShouldHaveSingleItem();
        image.Data.ToArray().ShouldBe(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public async Task Base64Image_StaysInlineDataContent()
    {
        var message = await BuildAsync(new ImageContentPartDto { Base64Data = Convert.ToBase64String([4, 5]), MediaType = "image/jpeg" });

        var image = message.Contents.OfType<DataContent>().ShouldHaveSingleItem();
        image.MediaType.ShouldBe("image/jpeg");
    }

    [Theory]
    [InlineData("ftp://example.com/cat.png", null)]
    [InlineData("https://example.com/cat.png", "text/html")]
    [InlineData("https://example.com/cat.png", "image/")]
    [InlineData("data:image/png;base64,@@@", null)]
    public async Task MalformedImage_Is400NotA500(string url, string? mediaType)
    {
        var ex = await Should.ThrowAsync<BusinessException>(() => BuildAsync(new ImageContentPartDto { Url = url, MediaType = mediaType }));

        ex.HttpStatusCode.ShouldBe(400);
        ex.Code.ShouldBe(ErrorCodes.InvalidContent);
    }

    /// <summary>
    /// 默认解析器不加载文件内容。此前它把文件块换成一行 "[File: name (ID: ...)]" 交给模型：
    /// 模型看不到文件却照样作答，调用方以为文件已送达。现在拒绝，且拒绝要说清是「不支持」而不是「请求有误」。
    /// </summary>
    [Fact]
    public async Task FileReference_IsRefusedAsUnsupportedInsteadOfBecomingAPlaceholder()
    {
        var ex = await Should.ThrowAsync<BusinessException>(() => BuildAsync(
            new TextContentPartDto { Text = "summarise the attachment" },
            new FileContentPartDto { FileId = Guid.NewGuid(), FileName = "report.pdf" }));

        ex.HttpStatusCode.ShouldBe(501);
        ex.Code.ShouldBe(ErrorCodes.UnsupportedMediaType);
    }

    [Fact]
    public async Task FileReference_WithEmptyId_IsStillA400()
    {
        var ex = await Should.ThrowAsync<BusinessException>(() => BuildAsync(new FileContentPartDto { FileId = Guid.Empty }));

        ex.HttpStatusCode.ShouldBe(400);
        ex.Code.ShouldBe(ErrorCodes.InvalidContent);
    }
}
