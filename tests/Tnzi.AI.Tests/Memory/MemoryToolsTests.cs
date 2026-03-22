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

public class BuiltInMemoryToolsTests
{
    private static Tnzi.AI.Tools.BuiltIn.MemoryTools CreateTools(
        Mock<IMemoryStore>? store = null,
        MemoryOptions? options = null)
    {
        store ??= new Mock<IMemoryStore>();
        var aiOptions = Microsoft.Extensions.Options.Options.Create(new AIOptions
        {
            ContextProviders = new ContextProvidersOptions
            {
                Memory = options ?? new MemoryOptions { EnableUserIsolation = false }
            }
        });
        return new Tnzi.AI.Tools.BuiltIn.MemoryTools(
            store.Object,
            Mock.Of<ILogger<Tnzi.AI.Tools.BuiltIn.MemoryTools>>(),
            aiOptions);
    }

    // ── save_memory ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveMemoryAsync_ValidContent_ReturnsStructuredSaved()
    {
        var store = new Mock<IMemoryStore>();
        var tools = CreateTools(store);

        var result = await tools.SaveMemoryAsync("User name is ABC", "fact", 0.9);

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"status\":\"saved\"");
        json.ShouldContain("\"category\":\"fact\"");
        store.Verify(s => s.AppendAsync(
            It.IsAny<MemoryScope>(), "User name is ABC", 0.9, "fact", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveMemoryAsync_EmptyContent_ReturnsStructuredError()
    {
        var tools = CreateTools();
        var result = await tools.SaveMemoryAsync("");
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"status\":\"error\"");
    }

    [Fact]
    public async Task SaveMemoryAsync_InvalidCategory_ReturnsStructuredError()
    {
        var tools = CreateTools();
        var result = await tools.SaveMemoryAsync("test", "invalid_cat");
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"status\":\"error\"");
        json.ShouldContain("invalid_cat");
    }

    [Fact]
    public async Task SaveMemoryAsync_ClampsImportance()
    {
        var store = new Mock<IMemoryStore>();
        var tools = CreateTools(store);

        await tools.SaveMemoryAsync("test", importance: 1.5);

        store.Verify(s => s.AppendAsync(
            It.IsAny<MemoryScope>(), "test", 1.0, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveMemoryAsync_ValidCustomCategory_WhenValidCategoriesConfigured()
    {
        var store = new Mock<IMemoryStore>();
        var options = new MemoryOptions
        {
            EnableUserIsolation = false,
            ValidCategories = ["preference", "fact", "rule"]
        };
        var tools = CreateTools(store, options);

        var result = await tools.SaveMemoryAsync("Always use UTC", "rule");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"status\":\"saved\"");
        store.Verify(s => s.AppendAsync(
            It.IsAny<MemoryScope>(), "Always use UTC", 0.7, "rule", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveMemoryAsync_InvalidCategoryWhenValidCategoriesEmpty_SavesAnyCategory()
    {
        // When ValidCategories is empty, no category validation is performed
        var store = new Mock<IMemoryStore>();
        var options = new MemoryOptions
        {
            EnableUserIsolation = false,
            ValidCategories = [],
            EnablePiiProtection = false
        };
        var tools = CreateTools(store, options);

        var result = await tools.SaveMemoryAsync("some info", "custom_type");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"status\":\"saved\"");
    }

    // ── PII protection ───────────────────────────────────────────────────────

    [Fact]
    public async Task SaveMemoryAsync_WithEmailAddress_ReturnsRejected()
    {
        var tools = CreateTools();
        var result = await tools.SaveMemoryAsync("Contact user at john.doe@example.com for info");
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"status\":\"rejected\"");
    }

    [Fact]
    public async Task SaveMemoryAsync_WithPhoneNumber_ReturnsRejected()
    {
        var tools = CreateTools();
        var result = await tools.SaveMemoryAsync("User phone is 416-555-1234");
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"status\":\"rejected\"");
    }

    [Fact]
    public async Task SaveMemoryAsync_WithSinPattern_ReturnsRejected()
    {
        var tools = CreateTools();
        var result = await tools.SaveMemoryAsync("SIN is 123 456 789");
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"status\":\"rejected\"");
    }

    [Fact]
    public async Task SaveMemoryAsync_WithBankAccount_ReturnsRejected()
    {
        var tools = CreateTools();
        var result = await tools.SaveMemoryAsync("Account number is 12345678");
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"status\":\"rejected\"");
    }

    [Fact]
    public async Task SaveMemoryAsync_PiiProtectionDisabled_AllowsPiiContent()
    {
        var store = new Mock<IMemoryStore>();
        var options = new MemoryOptions
        {
            EnableUserIsolation = false,
            EnablePiiProtection = false
        };
        var tools = CreateTools(store, options);

        var result = await tools.SaveMemoryAsync("Contact at test@example.com");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"status\":\"saved\"");
        store.Verify(s => s.AppendAsync(
            It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── search_memory ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchMemoryAsync_ReturnsStructuredResults()
    {
        var store = new Mock<IMemoryStore>();
        var entryId = Guid.NewGuid();
        store.Setup(s => s.SearchAsync(It.IsAny<MemoryScope>(), "name", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemorySearchResult>
            {
                new() { Id = entryId, Content = "User name is ABC", Category = "fact", Score = 0.85 }
            });
        var tools = CreateTools(store);

        var result = await tools.SearchMemoryAsync("name");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("User name is ABC");
        json.ShouldContain("fact");
        json.ShouldContain("\"count\":1");
        json.ShouldContain(entryId.ToString("N"));
    }

    [Fact]
    public async Task SearchMemoryAsync_NoResults_ReturnsEmptyStructured()
    {
        var store = new Mock<IMemoryStore>();
        store.Setup(s => s.SearchAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemorySearchResult>());
        var tools = CreateTools(store);

        var result = await tools.SearchMemoryAsync("nonexistent");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"count\":0");
        json.ShouldContain("No memories found");
    }

    [Fact]
    public async Task SearchMemoryAsync_WithCategory_UsesSearchByCategoryAsync()
    {
        var store = new Mock<IMemoryStore>();
        store.Setup(s => s.SearchByCategoryAsync(It.IsAny<string>(), "theme", "preference", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemorySearchResult>
            {
                new() { Id = Guid.NewGuid(), Content = "User likes dark theme", Category = "preference", Score = 0.9 }
            });
        var tools = CreateTools(store);

        var result = await tools.SearchMemoryAsync("theme", "preference");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("dark theme");
        store.Verify(s => s.SearchByCategoryAsync(
            It.IsAny<string>(), "theme", "preference", 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── update_memory ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateMemoryAsync_ValidId_ReturnsStructuredUpdated()
    {
        var store = new Mock<IMemoryStore>();
        var tools = CreateTools(store);
        var id = Guid.NewGuid();

        var result = await tools.UpdateMemoryAsync(id.ToString(), "Updated content");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"status\":\"updated\"");
        json.ShouldContain(id.ToString("N"));
        store.Verify(s => s.UpdateEntryAsync(
            It.IsAny<string>(), id, "Updated content", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateMemoryAsync_InvalidId_ReturnsStructuredError()
    {
        var tools = CreateTools();
        var result = await tools.UpdateMemoryAsync("not-a-guid", "Updated content");
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"status\":\"error\"");
        json.ShouldContain("Invalid entry ID");
    }

    [Fact]
    public async Task UpdateMemoryAsync_EmptyContent_ReturnsStructuredError()
    {
        var tools = CreateTools();
        var result = await tools.UpdateMemoryAsync(Guid.NewGuid().ToString(), "");
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"status\":\"error\"");
    }

    [Fact]
    public async Task UpdateMemoryAsync_WithPiiContent_ReturnsRejected()
    {
        var tools = CreateTools();
        var result = await tools.UpdateMemoryAsync(Guid.NewGuid().ToString(), "Email is user@example.com");
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"status\":\"rejected\"");
    }

    // ── delete_memory ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteMemoryAsync_ValidId_ReturnsStructuredDeleted()
    {
        var store = new Mock<IMemoryStore>();
        var tools = CreateTools(store);
        var id = Guid.NewGuid();

        var result = await tools.DeleteMemoryAsync(id.ToString());

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"status\":\"deleted\"");
        json.ShouldContain(id.ToString("N"));
    }

    [Fact]
    public async Task DeleteMemoryAsync_InvalidId_ReturnsStructuredError()
    {
        var tools = CreateTools();
        var result = await tools.DeleteMemoryAsync("not-a-guid");
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"status\":\"error\"");
        json.ShouldContain("Invalid entry ID");
    }
}
