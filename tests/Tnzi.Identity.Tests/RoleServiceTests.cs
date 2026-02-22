namespace Tnzi.Identity.Tests;

public class RoleServiceTests
{
    private readonly Mock<RoleManager<Role>> _roleManagerMock;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    private readonly RoleService _roleService;

    public RoleServiceTests()
    {
        // 配置 Mapster 映射
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var roleStore = new Mock<IRoleStore<Role>>();
        _roleManagerMock = new Mock<RoleManager<Role>>(roleStore.Object, null!, null!, null!, null!);

        _eventBusMock = new Mock<IEventBus>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(IEventBus))).Returns(_eventBusMock.Object);

        _roleService = new RoleService(_roleManagerMock.Object, _serviceProviderMock.Object);
    }

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_WithValidInput_ReturnsRoleDto()
    {
        // Arrange
        var input = new CreateRoleDto
        {
            Name = "Admin",
            Description = "Administrator role"
        };

        _roleManagerMock.Setup(x => x.RoleExistsAsync(input.Name))
            .ReturnsAsync(false);

        _roleManagerMock.Setup(x => x.CreateAsync(It.IsAny<Role>()))
            .ReturnsAsync((Role r) =>
            {
                r.Id = Guid.NewGuid();
                return IdentityResult.Success;
            });

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.RoleCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _roleService.CreateAsync(input);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(input.Name, result.Data.Name);
        Assert.Equal(input.Description, result.Data.Description);
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateName_ReturnsFailResult()
    {
        // Arrange
        var input = new CreateRoleDto
        {
            Name = "Admin",
            Description = "Duplicate role"
        };

        _roleManagerMock.Setup(x => x.RoleExistsAsync(input.Name))
            .ReturnsAsync(true);

        // Act
        var result = await _roleService.CreateAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Code);
        Assert.Contains("already exists", result.Message!);
        _roleManagerMock.Verify(x => x.CreateAsync(It.IsAny<Role>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WhenRoleManagerFails_ReturnsFailResult()
    {
        // Arrange
        var input = new CreateRoleDto
        {
            Name = "Admin",
            Description = "Some role"
        };

        _roleManagerMock.Setup(x => x.RoleExistsAsync(input.Name))
            .ReturnsAsync(false);

        _roleManagerMock.Setup(x => x.CreateAsync(It.IsAny<Role>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Creation failed" }));

        // Act
        var result = await _roleService.CreateAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_WithValidInput_ReturnsUpdatedRoleDto()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var existingRole = new Role
        {
            Id = roleId,
            Name = "OldName",
            Description = "Old description"
        };
        var input = new UpdateRoleDto
        {
            Name = "NewName",
            Description = "New description"
        };

        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync(existingRole);

        _roleManagerMock.Setup(x => x.UpdateAsync(existingRole))
            .ReturnsAsync(IdentityResult.Success);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.RoleUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _roleService.UpdateAsync(roleId, input);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(input.Name, result.Data.Name);
        Assert.Equal(input.Description, result.Data.Description);
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistentRole_ReturnsNotFound()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var input = new UpdateRoleDto { Name = "NewName" };

        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync((Role?)null);

        // Act
        var result = await _roleService.UpdateAsync(roleId, input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
        _roleManagerMock.Verify(x => x.UpdateAsync(It.IsAny<Role>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WhenRoleManagerFails_ReturnsFailResult()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var existingRole = new Role { Id = roleId, Name = "OldName" };
        var input = new UpdateRoleDto { Name = "NewName" };

        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync(existingRole);

        _roleManagerMock.Setup(x => x.UpdateAsync(existingRole))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Update failed" }));

        // Act
        var result = await _roleService.UpdateAsync(roleId, input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_WithValidId_DeletesRole()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var role = new Role { Id = roleId, Name = "TestRole" };

        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync(role);

        _roleManagerMock.Setup(x => x.DeleteAsync(role))
            .ReturnsAsync(IdentityResult.Success);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.RoleDeletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _roleService.DeleteAsync(roleId);

        // Assert
        Assert.True(result.Succeeded);
        _roleManagerMock.Verify(x => x.DeleteAsync(role), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentId_ReturnsNotFound()
    {
        // Arrange
        var roleId = Guid.NewGuid();

        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync((Role?)null);

        // Act
        var result = await _roleService.DeleteAsync(roleId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
        _roleManagerMock.Verify(x => x.DeleteAsync(It.IsAny<Role>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenRoleManagerFails_ReturnsFailResult()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var role = new Role { Id = roleId, Name = "TestRole" };

        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync(role);

        _roleManagerMock.Setup(x => x.DeleteAsync(role))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Delete failed" }));

        // Act
        var result = await _roleService.DeleteAsync(roleId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    #endregion

    #region GetByIdAsync

    [Fact]
    public async Task GetByIdAsync_WithValidId_ReturnsRoleDto()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var role = new Role
        {
            Id = roleId,
            Name = "Admin",
            Description = "Administrator"
        };

        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync(role);

        // Act
        var result = await _roleService.GetByIdAsync(roleId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(roleId, result.Data.Id);
        Assert.Equal("Admin", result.Data.Name);
        Assert.Equal("Administrator", result.Data.Description);
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistentId_ReturnsNotFound()
    {
        // Arrange
        var roleId = Guid.NewGuid();

        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync((Role?)null);

        // Act
        var result = await _roleService.GetByIdAsync(roleId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    #endregion

    #region GetByNameAsync

    [Fact]
    public async Task GetByNameAsync_WithValidName_ReturnsRoleDto()
    {
        // Arrange
        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = "Admin",
            Description = "Administrator"
        };

        _roleManagerMock.Setup(x => x.FindByNameAsync("Admin"))
            .ReturnsAsync(role);

        // Act
        var result = await _roleService.GetByNameAsync("Admin");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Admin", result.Data.Name);
    }

    [Fact]
    public async Task GetByNameAsync_WithNonExistentName_ReturnsNotFound()
    {
        // Arrange
        _roleManagerMock.Setup(x => x.FindByNameAsync("NonExistent"))
            .ReturnsAsync((Role?)null);

        // Act
        var result = await _roleService.GetByNameAsync("NonExistent");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    #endregion

    #region GetAllAsync

    [Fact]
    public async Task GetAllAsync_ReturnsAllRoles()
    {
        // Arrange
        var roles = new List<Role>
        {
            new Role { Id = Guid.NewGuid(), Name = "Admin", Description = "Administrator" },
            new Role { Id = Guid.NewGuid(), Name = "User", Description = "Regular user" }
        };

        var rolesQueryable = roles.BuildMock();
        _roleManagerMock.Setup(x => x.Roles).Returns(rolesQueryable);

        // Act
        var result = await _roleService.GetAllAsync();

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Count());
    }

    [Fact]
    public async Task GetAllAsync_WithNoRoles_ReturnsEmptyList()
    {
        // Arrange
        var roles = new List<Role>();
        var rolesQueryable = roles.BuildMock();
        _roleManagerMock.Setup(x => x.Roles).Returns(rolesQueryable);

        // Act
        var result = await _roleService.GetAllAsync();

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data);
    }

    #endregion

    #region ExistsAsync

    [Fact]
    public async Task ExistsAsync_WithExistingRole_ReturnsTrue()
    {
        // Arrange
        _roleManagerMock.Setup(x => x.RoleExistsAsync("Admin"))
            .ReturnsAsync(true);

        // Act
        var result = await _roleService.ExistsAsync("Admin");

        // Assert
        Assert.True(result.Succeeded);
        Assert.True(result.Data);
    }

    [Fact]
    public async Task ExistsAsync_WithNonExistentRole_ReturnsFalse()
    {
        // Arrange
        _roleManagerMock.Setup(x => x.RoleExistsAsync("NonExistent"))
            .ReturnsAsync(false);

        // Act
        var result = await _roleService.ExistsAsync("NonExistent");

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(result.Data);
    }

    #endregion
}
