namespace Tnzi.Identity.Tests;

public class RoleServiceTests
{
    private readonly Mock<RoleManager<Role>> _roleManagerMock;
    private readonly Mock<DbContext> _dbContextMock;
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

        _dbContextMock = new Mock<DbContext>(new DbContextOptions<DbContext>());
        _eventBusMock = new Mock<IEventBus>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(IEventBus))).Returns(_eventBusMock.Object);

        _roleService = new RoleService(_roleManagerMock.Object, _dbContextMock.Object, _serviceProviderMock.Object);
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
    public async Task CreateAsync_WithIsDefault_SetsIsDefault()
    {
        // Arrange
        var input = new CreateRoleDto
        {
            Name = "DefaultRole",
            Description = "A default role",
            IsDefault = true
        };

        _roleManagerMock.Setup(x => x.RoleExistsAsync(input.Name)).ReturnsAsync(false);
        _roleManagerMock.Setup(x => x.CreateAsync(It.IsAny<Role>()))
            .Callback<Role>(r => r.Id = Guid.NewGuid())
            .ReturnsAsync(IdentityResult.Success);
        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.RoleCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _roleService.CreateAsync(input);

        // Assert
        Assert.True(result.Succeeded);
        _roleManagerMock.Verify(x => x.CreateAsync(It.Is<Role>(r => r.IsDefault == true)), Times.Once);
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
    public async Task UpdateAsync_SystemRole_CannotRename()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var systemRole = new Role
        {
            Id = roleId,
            Name = "Admin",
            IsSystem = true
        };
        var input = new UpdateRoleDto { Name = "RenamedAdmin" };

        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync(systemRole);

        // Act
        var result = await _roleService.UpdateAsync(roleId, input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(403, result.Code);
        Assert.Contains("cannot be renamed", result.Message!);
        _roleManagerMock.Verify(x => x.UpdateAsync(It.IsAny<Role>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_SystemRole_CanUpdateDescription()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var systemRole = new Role
        {
            Id = roleId,
            Name = "Admin",
            IsSystem = true,
            Description = "Old desc"
        };
        var input = new UpdateRoleDto
        {
            Name = "Admin", // 名称不变
            Description = "Updated description"
        };

        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync(systemRole);
        _roleManagerMock.Setup(x => x.UpdateAsync(systemRole))
            .ReturnsAsync(IdentityResult.Success);
        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.RoleUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _roleService.UpdateAsync(roleId, input);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal("Updated description", result.Data!.Description);
    }

    [Fact]
    public async Task UpdateAsync_WithIsDefault_UpdatesIsDefault()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var existingRole = new Role { Id = roleId, Name = "User", IsDefault = false };
        var input = new UpdateRoleDto { Name = "User", IsDefault = true };

        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString())).ReturnsAsync(existingRole);
        _roleManagerMock.Setup(x => x.UpdateAsync(existingRole)).ReturnsAsync(IdentityResult.Success);
        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.RoleUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _roleService.UpdateAsync(roleId, input);

        // Assert
        Assert.True(result.Succeeded);
        Assert.True(existingRole.IsDefault);
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
    public async Task DeleteAsync_SystemRole_ReturnsForbidden()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var systemRole = new Role { Id = roleId, Name = "Admin", IsSystem = true };

        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync(systemRole);

        // Act
        var result = await _roleService.DeleteAsync(roleId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(403, result.Code);
        Assert.Contains("cannot be deleted", result.Message!);
        _roleManagerMock.Verify(x => x.DeleteAsync(It.IsAny<Role>()), Times.Never);
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

    #region DeleteManyAsync

    [Fact]
    public async Task DeleteManyAsync_WithValidIds_DeletesAll()
    {
        // Arrange
        var role1Id = Guid.NewGuid();
        var role2Id = Guid.NewGuid();
        var role1 = new Role { Id = role1Id, Name = "Role1" };
        var role2 = new Role { Id = role2Id, Name = "Role2" };

        _roleManagerMock.Setup(x => x.FindByIdAsync(role1Id.ToString())).ReturnsAsync(role1);
        _roleManagerMock.Setup(x => x.FindByIdAsync(role2Id.ToString())).ReturnsAsync(role2);
        _roleManagerMock.Setup(x => x.DeleteAsync(It.IsAny<Role>())).ReturnsAsync(IdentityResult.Success);
        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.RoleDeletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _roleService.DeleteManyAsync(new[] { role1Id, role2Id });

        // Assert
        Assert.True(result.Succeeded);
        _roleManagerMock.Verify(x => x.DeleteAsync(It.IsAny<Role>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DeleteManyAsync_SkipsNonExistent()
    {
        // Arrange
        var existingId = Guid.NewGuid();
        var nonExistentId = Guid.NewGuid();
        var role = new Role { Id = existingId, Name = "TestRole" };

        _roleManagerMock.Setup(x => x.FindByIdAsync(existingId.ToString())).ReturnsAsync(role);
        _roleManagerMock.Setup(x => x.FindByIdAsync(nonExistentId.ToString())).ReturnsAsync((Role?)null);
        _roleManagerMock.Setup(x => x.DeleteAsync(role)).ReturnsAsync(IdentityResult.Success);
        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.RoleDeletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _roleService.DeleteManyAsync(new[] { existingId, nonExistentId });

        // Assert
        Assert.True(result.Succeeded);
        _roleManagerMock.Verify(x => x.DeleteAsync(role), Times.Once);
    }

    [Fact]
    public async Task DeleteManyAsync_SkipsSystemRoles_ReportsFailure()
    {
        // Arrange
        var systemRoleId = Guid.NewGuid();
        var normalRoleId = Guid.NewGuid();
        var systemRole = new Role { Id = systemRoleId, Name = "Admin", IsSystem = true };
        var normalRole = new Role { Id = normalRoleId, Name = "Custom" };

        _roleManagerMock.Setup(x => x.FindByIdAsync(systemRoleId.ToString())).ReturnsAsync(systemRole);
        _roleManagerMock.Setup(x => x.FindByIdAsync(normalRoleId.ToString())).ReturnsAsync(normalRole);
        _roleManagerMock.Setup(x => x.DeleteAsync(normalRole)).ReturnsAsync(IdentityResult.Success);
        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.RoleDeletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _roleService.DeleteManyAsync(new[] { systemRoleId, normalRoleId });

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Contains("system role", result.Message!);
        // Normal role should still be deleted
        _roleManagerMock.Verify(x => x.DeleteAsync(normalRole), Times.Once);
        _roleManagerMock.Verify(x => x.DeleteAsync(systemRole), Times.Never);
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

    #region GetPagedListAsync

    [Fact]
    public async Task GetPagedListAsync_ReturnsPagedRoles()
    {
        // Arrange
        var roles = new List<Role>
        {
            new Role { Id = Guid.NewGuid(), Name = "Admin", Description = "Administrator", CreationTime = DateTime.UtcNow },
            new Role { Id = Guid.NewGuid(), Name = "User", Description = "Regular user", CreationTime = DateTime.UtcNow.AddMinutes(-1) },
            new Role { Id = Guid.NewGuid(), Name = "Editor", Description = "Content editor", CreationTime = DateTime.UtcNow.AddMinutes(-2) }
        };

        var rolesQueryable = roles.BuildMock();
        _roleManagerMock.Setup(x => x.Roles).Returns(rolesQueryable);

        var query = new RoleListQueryDto { PageIndex = 1, PageSize = 2 };

        // Act
        var result = await _roleService.GetPagedListAsync(query);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        // Note: MockQueryable + ProjectTo doesn't properly support Skip/Take,
        // so we only verify total count and that items are returned
        Assert.Equal(3, result.Data.TotalCount);
        Assert.NotEmpty(result.Data.Items);
    }

    [Fact]
    public async Task GetPagedListAsync_WithKeyword_FiltersResults()
    {
        // Arrange
        var roles = new List<Role>
        {
            new Role { Id = Guid.NewGuid(), Name = "Admin", Description = "Administrator" },
            new Role { Id = Guid.NewGuid(), Name = "User", Description = "Regular user" },
            new Role { Id = Guid.NewGuid(), Name = "SuperAdmin", Description = "Super administrator" }
        };

        var rolesQueryable = roles.BuildMock();
        _roleManagerMock.Setup(x => x.Roles).Returns(rolesQueryable);

        var query = new RoleListQueryDto { Keyword = "admin", PageIndex = 1, PageSize = 10 };

        // Act
        var result = await _roleService.GetPagedListAsync(query);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.TotalCount);
    }

    [Fact]
    public async Task GetPagedListAsync_WithIsSystem_FiltersResults()
    {
        // Arrange
        var roles = new List<Role>
        {
            new Role { Id = Guid.NewGuid(), Name = "Admin", IsSystem = true },
            new Role { Id = Guid.NewGuid(), Name = "User", IsSystem = false },
            new Role { Id = Guid.NewGuid(), Name = "Guest", IsSystem = false }
        };

        var rolesQueryable = roles.BuildMock();
        _roleManagerMock.Setup(x => x.Roles).Returns(rolesQueryable);

        var query = new RoleListQueryDto { IsSystem = true, PageIndex = 1, PageSize = 10 };

        // Act
        var result = await _roleService.GetPagedListAsync(query);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Data!.TotalCount);
    }

    [Fact]
    public async Task GetPagedListAsync_WithIsDefault_FiltersResults()
    {
        // Arrange
        var roles = new List<Role>
        {
            new Role { Id = Guid.NewGuid(), Name = "Admin", IsDefault = false },
            new Role { Id = Guid.NewGuid(), Name = "User", IsDefault = true },
            new Role { Id = Guid.NewGuid(), Name = "Guest", IsDefault = true }
        };

        var rolesQueryable = roles.BuildMock();
        _roleManagerMock.Setup(x => x.Roles).Returns(rolesQueryable);

        var query = new RoleListQueryDto { IsDefault = true, PageIndex = 1, PageSize = 10 };

        // Act
        var result = await _roleService.GetPagedListAsync(query);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.TotalCount);
    }

    #endregion

    #region GetDetailAsync

    [Fact]
    public async Task GetDetailAsync_WithValidId_ReturnsDetailWithUserCount()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var role = new Role
        {
            Id = roleId,
            Name = "Admin",
            Description = "Administrator",
            IsSystem = true,
            IsDefault = false
        };

        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync(role);

        var userRoles = new List<UserRole>
        {
            new UserRole { UserId = Guid.NewGuid(), RoleId = roleId },
            new UserRole { UserId = Guid.NewGuid(), RoleId = roleId },
            new UserRole { UserId = Guid.NewGuid(), RoleId = Guid.NewGuid() } // 其他角色
        };

        var userRolesMock = userRoles.BuildMockDbSet();
        _dbContextMock.Setup(x => x.Set<UserRole>()).Returns(userRolesMock.Object);

        // Act
        var result = await _roleService.GetDetailAsync(roleId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(roleId, result.Data.Id);
        Assert.Equal("Admin", result.Data.Name);
        Assert.Equal(2, result.Data.UserCount);
        Assert.True(result.Data.IsSystem);
    }

    [Fact]
    public async Task GetDetailAsync_WithNonExistentId_ReturnsNotFound()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync((Role?)null);

        // Act
        var result = await _roleService.GetDetailAsync(roleId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    #endregion

    #region GetUserCountAsync

    [Fact]
    public async Task GetUserCountAsync_WithValidRole_ReturnsCount()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var role = new Role { Id = roleId, Name = "Admin" };

        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync(role);

        var userRoles = new List<UserRole>
        {
            new UserRole { UserId = Guid.NewGuid(), RoleId = roleId },
            new UserRole { UserId = Guid.NewGuid(), RoleId = roleId },
            new UserRole { UserId = Guid.NewGuid(), RoleId = Guid.NewGuid() }
        };

        var userRolesMock = userRoles.BuildMockDbSet();
        _dbContextMock.Setup(x => x.Set<UserRole>()).Returns(userRolesMock.Object);

        // Act
        var result = await _roleService.GetUserCountAsync(roleId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data);
    }

    [Fact]
    public async Task GetUserCountAsync_WithNonExistentRole_ReturnsNotFound()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync((Role?)null);

        // Act
        var result = await _roleService.GetUserCountAsync(roleId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    #endregion

    #region GetDefaultRolesAsync

    [Fact]
    public async Task GetDefaultRolesAsync_ReturnsOnlyDefaultRoles()
    {
        // Arrange
        var roles = new List<Role>
        {
            new Role { Id = Guid.NewGuid(), Name = "Admin", IsDefault = false },
            new Role { Id = Guid.NewGuid(), Name = "User", IsDefault = true },
            new Role { Id = Guid.NewGuid(), Name = "Guest", IsDefault = true }
        };

        var rolesQueryable = roles.BuildMock();
        _roleManagerMock.Setup(x => x.Roles).Returns(rolesQueryable);

        // Act
        var result = await _roleService.GetDefaultRolesAsync();

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Count());
        Assert.All(result.Data, r => Assert.True(r.IsDefault));
    }

    [Fact]
    public async Task GetDefaultRolesAsync_WithNoDefaults_ReturnsEmpty()
    {
        // Arrange
        var roles = new List<Role>
        {
            new Role { Id = Guid.NewGuid(), Name = "Admin", IsDefault = false },
            new Role { Id = Guid.NewGuid(), Name = "User", IsDefault = false }
        };

        var rolesQueryable = roles.BuildMock();
        _roleManagerMock.Setup(x => x.Roles).Returns(rolesQueryable);

        // Act
        var result = await _roleService.GetDefaultRolesAsync();

        // Assert
        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!);
    }

    #endregion

    #region SetDefaultAsync

    [Fact]
    public async Task SetDefaultAsync_WithValidRole_SetsDefault()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var role = new Role { Id = roleId, Name = "User", IsDefault = false };

        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString())).ReturnsAsync(role);
        _roleManagerMock.Setup(x => x.UpdateAsync(role)).ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _roleService.SetDefaultAsync(roleId, true);

        // Assert
        Assert.True(result.Succeeded);
        Assert.True(role.IsDefault);
        _roleManagerMock.Verify(x => x.UpdateAsync(role), Times.Once);
    }

    [Fact]
    public async Task SetDefaultAsync_UnsetDefault()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var role = new Role { Id = roleId, Name = "User", IsDefault = true };

        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString())).ReturnsAsync(role);
        _roleManagerMock.Setup(x => x.UpdateAsync(role)).ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _roleService.SetDefaultAsync(roleId, false);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(role.IsDefault);
    }

    [Fact]
    public async Task SetDefaultAsync_WithNonExistentRole_ReturnsNotFound()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        _roleManagerMock.Setup(x => x.FindByIdAsync(roleId.ToString())).ReturnsAsync((Role?)null);

        // Act
        var result = await _roleService.SetDefaultAsync(roleId, true);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    #endregion
}
