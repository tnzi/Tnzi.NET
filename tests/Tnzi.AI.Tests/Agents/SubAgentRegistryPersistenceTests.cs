namespace Tnzi.AI.Tests.Agents;

public class SubAgentRegistryPersistenceTests
{
    private readonly SubAgentRegistry _registry;
    private readonly Mock<IRepository<SubAgentType, Guid>> _mockRepo;

    public SubAgentRegistryPersistenceTests()
    {
        _registry = new SubAgentRegistry();
        _mockRepo = new Mock<IRepository<SubAgentType, Guid>>();
    }

    [Fact]
    public async Task LoadFromStoreAsync_MergesDbTypesWithBuiltIn()
    {
        // Arrange
        var dbTypes = new List<SubAgentType>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "code-reviewer",
                Description = "Reviews code for quality",
                ToolGroups = ["code-analysis", "file"],
                ExcludedToolGroups = null,
                MaxTurns = 20,
                Instructions = "Review code carefully.",
                IsEnabled = true
            }
        };

        _mockRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(dbTypes.BuildMock());

        // Act
        await _registry.LoadFromStoreAsync(_mockRepo.Object);

        // Assert - built-in types still present
        _registry.Get("general-purpose").ShouldNotBeNull();
        _registry.Get("bash").ShouldNotBeNull();
        _registry.Get("researcher").ShouldNotBeNull();

        // DB type added
        var codeReviewer = _registry.Get("code-reviewer");
        codeReviewer.ShouldNotBeNull();
        codeReviewer.Description.ShouldBe("Reviews code for quality");
        codeReviewer.MaxTurns.ShouldBe(20);
        codeReviewer.ToolGroups.ShouldContain("code-analysis");
        codeReviewer.ToolGroups.ShouldContain("file");

        // Total = 3 built-in + 1 custom
        _registry.GetAll().Count.ShouldBe(4);
    }

    [Fact]
    public async Task LoadFromStoreAsync_DbTypeOverridesBuiltIn_WhenSameName()
    {
        // Arrange
        var dbTypes = new List<SubAgentType>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "bash",
                Description = "Custom bash agent with extended tools",
                ToolGroups = ["sandbox", "custom-tool"],
                ExcludedToolGroups = [],
                MaxTurns = 10,
                Instructions = "Custom bash instructions.",
                DefaultApprovalMode = ToolApprovalMode.AlwaysRequire,
                CapabilityTags = ["shell", "custom"],
                IsEnabled = true
            }
        };

        _mockRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(dbTypes.BuildMock());

        // Act
        await _registry.LoadFromStoreAsync(_mockRepo.Object);

        // Assert - built-in bash is overridden
        var bash = _registry.Get("bash");
        bash.ShouldNotBeNull();
        bash.Description.ShouldBe("Custom bash agent with extended tools");
        bash.MaxTurns.ShouldBe(10);
        bash.ToolGroups.ShouldContain("custom-tool");
        bash.DefaultApprovalMode.ShouldBe(ToolApprovalMode.AlwaysRequire);
        bash.CapabilityTags.ShouldNotBeNull();
        bash.CapabilityTags.ShouldContain("custom");

        // Total still 3 (override, not add)
        _registry.GetAll().Count.ShouldBe(3);
    }

    [Fact]
    public async Task LoadFromStoreAsync_SkipsDisabledTypes()
    {
        // Arrange
        var dbTypes = new List<SubAgentType>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "disabled-type",
                Description = "Should not appear",
                MaxTurns = 10,
                IsEnabled = false
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "enabled-type",
                Description = "Should appear",
                MaxTurns = 15,
                IsEnabled = true
            }
        };

        _mockRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(dbTypes.BuildMock());

        // Act
        await _registry.LoadFromStoreAsync(_mockRepo.Object);

        // Assert
        _registry.Get("disabled-type").ShouldBeNull();
        _registry.Get("enabled-type").ShouldNotBeNull();
    }

    [Fact]
    public async Task LoadFromStoreAsync_HandlesNullListFields()
    {
        // Arrange
        var dbTypes = new List<SubAgentType>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "minimal-type",
                Description = "Minimal config",
                ToolGroups = null,
                ExcludedToolGroups = null,
                CapabilityTags = null,
                MaxTurns = 25,
                IsEnabled = true
            }
        };

        _mockRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(dbTypes.BuildMock());

        // Act
        await _registry.LoadFromStoreAsync(_mockRepo.Object);

        // Assert - null 列表归一化为空列表
        var type = _registry.Get("minimal-type");
        type.ShouldNotBeNull();
        type.ToolGroups.Count.ShouldBe(0);
        type.ExcludedToolGroups.Count.ShouldBe(0);
        type.CapabilityTags.ShouldNotBeNull();
        type.CapabilityTags.Count.ShouldBe(0);
    }

    [Fact]
    public async Task LoadFromStoreAsync_HandlesEmptyListFields()
    {
        // Arrange
        var dbTypes = new List<SubAgentType>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "empty-list-type",
                Description = "Empty lists",
                ToolGroups = [],
                ExcludedToolGroups = [],
                CapabilityTags = [],
                MaxTurns = 30,
                IsEnabled = true
            }
        };

        _mockRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(dbTypes.BuildMock());

        // Act
        await _registry.LoadFromStoreAsync(_mockRepo.Object);

        // Assert
        var type = _registry.Get("empty-list-type");
        type.ShouldNotBeNull();
        type.ToolGroups.Count.ShouldBe(0);
        type.ExcludedToolGroups.Count.ShouldBe(0);
    }

    [Fact]
    public async Task LoadFromStoreAsync_MapsDefaultApprovalMode()
    {
        // Arrange
        var dbTypes = new List<SubAgentType>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "approval-test",
                Description = "Tests approval mode mapping",
                MaxTurns = 10,
                DefaultApprovalMode = ToolApprovalMode.NeverRequire,
                IsEnabled = true
            }
        };

        _mockRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(dbTypes.BuildMock());

        // Act
        await _registry.LoadFromStoreAsync(_mockRepo.Object);

        // Assert
        var type = _registry.Get("approval-test");
        type.ShouldNotBeNull();
        type.DefaultApprovalMode.ShouldBe(ToolApprovalMode.NeverRequire);
    }

    [Fact]
    public async Task LoadFromStoreAsync_NullApprovalMode_MapsToNull()
    {
        // Arrange
        var dbTypes = new List<SubAgentType>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "null-approval",
                Description = "No approval mode",
                MaxTurns = 10,
                DefaultApprovalMode = null,
                IsEnabled = true
            }
        };

        _mockRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(dbTypes.BuildMock());

        // Act
        await _registry.LoadFromStoreAsync(_mockRepo.Object);

        // Assert
        var type = _registry.Get("null-approval");
        type.ShouldNotBeNull();
        type.DefaultApprovalMode.ShouldBeNull();
    }
}
