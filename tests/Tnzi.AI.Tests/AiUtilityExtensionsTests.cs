namespace Tnzi.AI.Tests;

public class AiUtilityExtensionsTests
{
    private static Mock<IAiUtility> CreateMock(string? returnValue)
    {
        var mock = new Mock<IAiUtility>();
        mock.Setup(x => x.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnValue);
        return mock;
    }

    [Fact]
    public async Task GenerateTitleAsync_CallsExecuteWithCorrectPrompt()
    {
        var mock = CreateMock("Generated Title");

        var result = await mock.Object.GenerateTitleAsync("Tell me about cats");

        result.ShouldBe("Generated Title");
        mock.Verify(x => x.ExecuteAsync(
            It.Is<string>(s => s.Contains("title") || s.Contains("Title")),
            It.Is<string>(s => s.Contains("Tell me about cats")),
            It.IsAny<AiUtilityCallOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateTitleAsync_TruncatesLongResult()
    {
        var longString = new string('A', 100);
        var mock = CreateMock(longString);

        var result = await mock.Object.GenerateTitleAsync("content", maxLength: 50);

        result.ShouldNotBeNull();
        result.ShouldEndWith("...");
        result!.Length.ShouldBeLessThanOrEqualTo(53); // 50 chars + "..."
    }

    [Fact]
    public async Task GenerateTitleAsync_StripsQuotes()
    {
        var mock = CreateMock("\"Some Quoted Title\"");

        var result = await mock.Object.GenerateTitleAsync("content");

        result.ShouldBe("Some Quoted Title");
    }

    [Fact]
    public async Task GenerateTitleAsync_WhenNullResponse_ReturnsNull()
    {
        var mock = CreateMock(null);

        var result = await mock.Object.GenerateTitleAsync("content");

        result.ShouldBeNull();
    }
}
