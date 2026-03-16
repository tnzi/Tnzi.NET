namespace Tnzi.AI.Tests.Memory;

public class MemoryToolsTests
{
    [Fact]
    public async Task WriteMemoryAsync_WithoutConfig_UsesSharedScopeByDefault()
    {
        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(x => x.Id).Returns(userId);

        var store = new Mock<IMemoryStore>();
        var tools = new Tnzi.AI.Coder.Memory.MemoryTools(
            store.Object,
            Mock.Of<ILogger<Tnzi.AI.Coder.Memory.MemoryTools>>(),
            currentUser.Object,
            configuration: null);

        await tools.WriteMemoryAsync("shared", "default");

        store.Verify(s => s.WriteAsync(
            It.Is<MemoryScope>(scope => scope.Name == "default" && scope.UserId == null),
            "shared",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WriteMemoryAsync_UserIsolationDisabled_WritesSharedScope()
    {
        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(x => x.Id).Returns(userId);

        var store = new Mock<IMemoryStore>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:ContextProviders:Memory:EnableUserIsolation"] = "false"
            })
            .Build();

        var tools = new Tnzi.AI.Coder.Memory.MemoryTools(
            store.Object,
            Mock.Of<ILogger<Tnzi.AI.Coder.Memory.MemoryTools>>(),
            currentUser.Object,
            configuration);

        await tools.WriteMemoryAsync("shared", "default");

        store.Verify(s => s.WriteAsync(
            It.Is<MemoryScope>(scope => scope.Name == "default" && scope.UserId == null),
            "shared",
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
