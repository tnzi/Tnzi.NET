using Tnzi.AI.Rag.FileExtractors;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// ImageContentExtractor 单元测试 — 验证图片内容提取和优雅降级
/// </summary>
public class ImageContentExtractorTests
{
    #region Supports

    [Theory]
    [InlineData("photo.png", true)]
    [InlineData("photo.jpg", true)]
    [InlineData("photo.jpeg", true)]
    [InlineData("image.gif", true)]
    [InlineData("image.webp", true)]
    [InlineData("icon.svg", true)]
    [InlineData("photo.PNG", true)]
    [InlineData("photo.JPG", true)]
    [InlineData("photo.Jpeg", true)]
    [InlineData("test.txt", false)]
    [InlineData("test.pdf", false)]
    [InlineData("test.csv", false)]
    [InlineData("test.docx", false)]
    [InlineData("test", false)]
    [InlineData("", false)]
    public void Supports_CorrectExtensions(string fileName, bool expected)
    {
        var extractor = new ImageContentExtractor();

        extractor.Supports(fileName).ShouldBe(expected);
    }

    #endregion

    #region ExtractTextAsync - Fallback (no ChatClientFactory)

    [Fact]
    public async Task ExtractText_NoChatClientFactory_ReturnsFallback()
    {
        var extractor = new ImageContentExtractor();
        using var stream = new MemoryStream([0x89, 0x50, 0x4E, 0x47]); // PNG magic bytes

        var result = await extractor.ExtractTextAsync(stream, "photo.png");

        result.ShouldBe("[Image: photo.png]");
    }

    [Fact]
    public async Task ExtractText_NoChatClientFactory_UsesFileNameOnly()
    {
        var extractor = new ImageContentExtractor();
        using var stream = new MemoryStream([0xFF, 0xD8, 0xFF]); // JPEG magic bytes

        var result = await extractor.ExtractTextAsync(stream, "/path/to/photo.jpg");

        result.ShouldBe("[Image: photo.jpg]");
    }

    [Fact]
    public async Task ExtractText_EmptyStream_ReturnsFallback()
    {
        var mockFactory = new Mock<IChatClientFactory>();
        var extractor = new ImageContentExtractor(mockFactory.Object);
        using var stream = new MemoryStream();

        var result = await extractor.ExtractTextAsync(stream, "empty.png");

        result.ShouldBe("[Image: empty.png]");
    }

    #endregion

    #region ExtractTextAsync - Vision Success

    [Fact]
    public async Task ExtractText_WithVision_ReturnsDescription()
    {
        // Arrange
        var description = "A sunset over the ocean with orange and purple hues.";
        var mockChatClient = CreateMockChatClient(description);
        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(null, null)).Returns(mockChatClient);

        var extractor = new ImageContentExtractor(mockFactory.Object);
        using var stream = new MemoryStream([0x89, 0x50, 0x4E, 0x47]); // PNG magic bytes

        // Act
        var result = await extractor.ExtractTextAsync(stream, "sunset.png");

        // Assert
        result.ShouldBe("[Image: sunset.png]\nA sunset over the ocean with orange and purple hues.");
    }

    [Fact]
    public async Task ExtractText_VisionReturnsEmpty_ReturnsFallback()
    {
        // Arrange
        var mockChatClient = CreateMockChatClient("");
        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(null, null)).Returns(mockChatClient);

        var extractor = new ImageContentExtractor(mockFactory.Object);
        using var stream = new MemoryStream([0x89, 0x50, 0x4E, 0x47]);

        // Act
        var result = await extractor.ExtractTextAsync(stream, "blank.png");

        // Assert
        result.ShouldBe("[Image: blank.png]");
    }

    [Fact]
    public async Task ExtractText_VisionReturnsNull_ReturnsFallback()
    {
        // Arrange
        var mockChatClient = CreateMockChatClient(null);
        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(null, null)).Returns(mockChatClient);

        var extractor = new ImageContentExtractor(mockFactory.Object);
        using var stream = new MemoryStream([0x89, 0x50, 0x4E, 0x47]);

        // Act
        var result = await extractor.ExtractTextAsync(stream, "null.png");

        // Assert
        result.ShouldBe("[Image: null.png]");
    }

    #endregion

    #region ExtractTextAsync - Error Handling

    [Fact]
    public async Task ExtractText_VisionThrows_ReturnsFallback()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Model not available"));

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(null, null)).Returns(mockChatClient.Object);

        var extractor = new ImageContentExtractor(mockFactory.Object);
        using var stream = new MemoryStream([0x89, 0x50, 0x4E, 0x47]);

        // Act
        var result = await extractor.ExtractTextAsync(stream, "error.png");

        // Assert
        result.ShouldBe("[Image: error.png]");
    }

    [Fact]
    public async Task ExtractText_Cancelled_ThrowsOperationCanceled()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(null, null)).Returns(mockChatClient.Object);

        var extractor = new ImageContentExtractor(mockFactory.Object);
        using var stream = new MemoryStream([0x89, 0x50, 0x4E, 0x47]);

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => extractor.ExtractTextAsync(stream, "cancel.png", cts.Token));
    }

    #endregion

    #region Helpers

    private static IChatClient CreateMockChatClient(string? responseText)
    {
        var mockClient = new Mock<IChatClient>();

        var chatResponse = new ChatResponse(
            responseText != null ? [new ChatMessage(ChatRole.Assistant, responseText)] : []);

        mockClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        return mockClient.Object;
    }

    #endregion
}
