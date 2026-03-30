namespace Tnzi.AI.Tests;

/// <summary>
/// AgentPersona 服务测试
/// </summary>
public class AgentPersonaServiceTests
{
    private readonly Mock<IRepository<AgentPersona, Guid>> _personaRepo;
    private readonly IServiceProvider _serviceProvider;

    public AgentPersonaServiceTests()
    {
        _personaRepo = new Mock<IRepository<AgentPersona, Guid>>();

        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    private AgentPersonaService CreateService() => new(_serviceProvider, _personaRepo.Object);

    [Fact]
    public async Task CreateAsync_ValidInput_ReturnsOk()
    {
        // Arrange
        var input = new CreateAgentPersonaDto
        {
            Name = "Helpful Assistant",
            Slug = "helpful-assistant",
            Content = "You are a helpful assistant."
        };
        _personaRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<AgentPersona, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentPersona?)null);
        _personaRepo.Setup(r => r.InsertAsync(It.IsAny<AgentPersona>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await CreateService().CreateAsync(input);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal("helpful-assistant", result.Data!.Slug);
    }

    [Fact]
    public async Task GetBySlugAsync_Exists_ReturnsOk()
    {
        // Arrange
        var persona = new AgentPersona
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Slug = "test",
            Content = "test content"
        };
        _personaRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<AgentPersona, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(persona);

        // Act
        var result = await CreateService().GetBySlugAsync("test");

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal("test", result.Data!.Slug);
    }

    [Fact]
    public async Task GetBySlugAsync_NotFound_ReturnsFail()
    {
        _personaRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<AgentPersona, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentPersona?)null);

        var result = await CreateService().GetBySlugAsync("nonexistent");

        Assert.True(result.Failed);
    }

    [Fact]
    public async Task DeleteAsync_Exists_ReturnsOk()
    {
        var persona = new AgentPersona { Id = Guid.NewGuid(), Name = "Test", Slug = "test", Content = "c" };
        _personaRepo.Setup(r => r.GetAsync(persona.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(persona);

        var result = await CreateService().DeleteAsync(persona.Id);

        Assert.True(result.Succeeded);
        _personaRepo.Verify(r => r.DeleteAsync(persona.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_SystemPersona_ReturnsFail()
    {
        var persona = new AgentPersona { Id = Guid.NewGuid(), Name = "System", Slug = "system", Content = "c", IsSystem = true };
        _personaRepo.Setup(r => r.GetAsync(persona.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(persona);

        var result = await CreateService().DeleteAsync(persona.Id);

        Assert.True(result.Failed);
        _personaRepo.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
