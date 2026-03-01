
namespace Tnzi.Authorization.Tests.Services;

/// <summary>
/// Tests for reverse permission query and authorization statistics
/// </summary>
public class AuthorizationEnhancedTests
{
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    public AuthorizationEnhancedTests()
    {
        _serviceProviderMock = new Mock<IServiceProvider>();

        // 设置 ILoggerFactory，ApplicationService.Logger 需要
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
    }

    #region GetPermissionRolesAsync Tests

    [Fact]
    public void IFunctionAuthorizationService_GetPermissionRolesAsync_MethodExists()
    {
        // Verify the interface method exists
        var method = typeof(Tnzi.Authorization.Services.IFunctionAuthorizationService).GetMethod("GetPermissionRolesAsync");
        method.ShouldNotBeNull();
        method!.ReturnType.ShouldBe(typeof(Task<Result<IEnumerable<PermissionRoleDto>>>));
    }

    [Fact]
    public async Task GetPermissionRolesAsync_WithEmptyPermissionName_ReturnsFail()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetPermissionRolesAsync("");

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task GetPermissionRolesAsync_WithWhitespacePermissionName_ReturnsFail()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetPermissionRolesAsync("   ");

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    #endregion

    #region GetPermissionUsersAsync Tests

    [Fact]
    public void IFunctionAuthorizationService_GetPermissionUsersAsync_MethodExists()
    {
        // Verify the interface method exists
        var method = typeof(Tnzi.Authorization.Services.IFunctionAuthorizationService).GetMethod("GetPermissionUsersAsync");
        method.ShouldNotBeNull();
        method!.ReturnType.ShouldBe(typeof(Task<Result<IEnumerable<PermissionUserDto>>>));
    }

    [Fact]
    public async Task GetPermissionUsersAsync_WithEmptyPermissionName_ReturnsFail()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetPermissionUsersAsync("");

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task GetPermissionUsersAsync_WithNullPermissionName_ReturnsFail()
    {
        // Arrange - no IUserRoleService provided
        var service = CreateService(userRoleService: null);

        // Act
        var result = await service.GetPermissionUsersAsync(null!);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    #endregion

    #region GetStatisticsAsync Tests

    [Fact]
    public void IFunctionAuthorizationService_GetStatisticsAsync_MethodExists()
    {
        // Verify the interface method exists
        var method = typeof(Tnzi.Authorization.Services.IFunctionAuthorizationService).GetMethod("GetStatisticsAsync");
        method.ShouldNotBeNull();
        method!.ReturnType.ShouldBe(typeof(Task<Result<AuthorizationStatisticsDto>>));
    }

    #endregion

    #region PermissionRoleDto Tests

    [Fact]
    public void PermissionRoleDto_DefaultValues()
    {
        // Act
        var dto = new PermissionRoleDto();

        // Assert
        dto.RoleId.ShouldBe(Guid.Empty);
        dto.IsEnabled.ShouldBeFalse();
        dto.FunctionId.ShouldBe(Guid.Empty);
        dto.FunctionCode.ShouldBe(string.Empty);
        dto.FunctionName.ShouldBe(string.Empty);
    }

    [Fact]
    public void PermissionRoleDto_CanSetValues()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var functionId = Guid.NewGuid();

        // Act
        var dto = new PermissionRoleDto
        {
            RoleId = roleId,
            IsEnabled = true,
            FunctionId = functionId,
            FunctionCode = "users.create",
            FunctionName = "Create User"
        };

        // Assert
        dto.RoleId.ShouldBe(roleId);
        dto.IsEnabled.ShouldBeTrue();
        dto.FunctionId.ShouldBe(functionId);
        dto.FunctionCode.ShouldBe("users.create");
        dto.FunctionName.ShouldBe("Create User");
    }

    #endregion

    #region PermissionUserDto Tests

    [Fact]
    public void PermissionUserDto_DefaultValues()
    {
        // Act
        var dto = new PermissionUserDto();

        // Assert
        dto.UserId.ShouldBe(Guid.Empty);
        dto.RoleIds.ShouldNotBeNull();
        dto.RoleIds.Count.ShouldBe(0);
    }

    [Fact]
    public void PermissionUserDto_CanTrackMultipleRoles()
    {
        // Arrange
        var roleId1 = Guid.NewGuid();
        var roleId2 = Guid.NewGuid();

        // Act
        var dto = new PermissionUserDto
        {
            UserId = Guid.NewGuid(),
            RoleIds = new List<Guid> { roleId1, roleId2 }
        };

        // Assert
        dto.RoleIds.Count.ShouldBe(2);
        dto.RoleIds.ShouldContain(roleId1);
        dto.RoleIds.ShouldContain(roleId2);
    }

    #endregion

    #region AuthorizationStatisticsDto Tests

    [Fact]
    public void AuthorizationStatisticsDto_DefaultValues_AreZero()
    {
        // Act
        var dto = new AuthorizationStatisticsDto();

        // Assert
        dto.TotalModules.ShouldBe(0);
        dto.EnabledModules.ShouldBe(0);
        dto.TotalFunctions.ShouldBe(0);
        dto.EnabledFunctions.ShouldBe(0);
        dto.TotalRoleFunctionAssignments.ShouldBe(0);
        dto.EnabledRoleFunctionAssignments.ShouldBe(0);
        dto.TotalModuleRoleAssignments.ShouldBe(0);
        dto.TotalModuleUserAssignments.ShouldBe(0);
    }

    [Fact]
    public void AuthorizationStatisticsDto_CanSetValues()
    {
        // Act
        var dto = new AuthorizationStatisticsDto
        {
            TotalModules = 10,
            EnabledModules = 8,
            TotalFunctions = 50,
            EnabledFunctions = 45,
            TotalRoleFunctionAssignments = 200,
            EnabledRoleFunctionAssignments = 180,
            TotalModuleRoleAssignments = 30,
            TotalModuleUserAssignments = 15
        };

        // Assert
        dto.TotalModules.ShouldBe(10);
        dto.EnabledModules.ShouldBe(8);
        dto.TotalFunctions.ShouldBe(50);
        dto.EnabledFunctions.ShouldBe(45);
        dto.TotalRoleFunctionAssignments.ShouldBe(200);
        dto.EnabledRoleFunctionAssignments.ShouldBe(180);
        dto.TotalModuleRoleAssignments.ShouldBe(30);
        dto.TotalModuleUserAssignments.ShouldBe(15);
    }

    #endregion

    #region CompareRolePermissionsAsync Tests

    [Fact]
    public async Task CompareRolePermissionsAsync_BothRolesEmpty_ReturnsEmptyLists()
    {
        // Arrange
        var roleId1 = Guid.NewGuid();
        var roleId2 = Guid.NewGuid();

        var roleFunctionMock = CreateMockRoleFunctionRepository(new List<RoleFunction>());
        var moduleFunctionMock = CreateMockModuleFunctionRepository(new List<ModuleFunction>());

        var service = CreateService(
            roleFunctionRepository: roleFunctionMock,
            moduleFunctionRepository: moduleFunctionMock);

        // Act
        var result = await service.CompareRolePermissionsAsync(roleId1, roleId2);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.RoleId1.ShouldBe(roleId1);
        result.Data.RoleId2.ShouldBe(roleId2);
        result.Data.OnlyInRole1.ShouldBeEmpty();
        result.Data.OnlyInRole2.ShouldBeEmpty();
        result.Data.Shared.ShouldBeEmpty();
    }

    [Fact]
    public async Task CompareRolePermissionsAsync_WithOverlap_ReturnsCorrectSets()
    {
        // Arrange
        var roleId1 = Guid.NewGuid();
        var roleId2 = Guid.NewGuid();
        var sharedFuncId = Guid.NewGuid();
        var onlyRole1FuncId = Guid.NewGuid();
        var onlyRole2FuncId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();

        var roleFunctions = new List<RoleFunction>
        {
            new() { RoleId = roleId1, FunctionId = sharedFuncId, IsEnabled = true },
            new() { RoleId = roleId1, FunctionId = onlyRole1FuncId, IsEnabled = true },
            new() { RoleId = roleId2, FunctionId = sharedFuncId, IsEnabled = true },
            new() { RoleId = roleId2, FunctionId = onlyRole2FuncId, IsEnabled = true }
        };

        var moduleFunctions = new List<ModuleFunction>
        {
            new() { Id = sharedFuncId, Code = "shared.func", Name = "Shared Function", ModuleId = moduleId, FunctionModule = new FunctionModule { Code = "mod1" } },
            new() { Id = onlyRole1FuncId, Code = "role1.func", Name = "Role1 Only", ModuleId = moduleId, FunctionModule = new FunctionModule { Code = "mod1" } },
            new() { Id = onlyRole2FuncId, Code = "role2.func", Name = "Role2 Only", ModuleId = moduleId, FunctionModule = new FunctionModule { Code = "mod1" } }
        };

        var roleFunctionMock = CreateMockRoleFunctionRepository(roleFunctions);
        var moduleFunctionMock = CreateMockModuleFunctionRepository(moduleFunctions);

        var service = CreateService(
            roleFunctionRepository: roleFunctionMock,
            moduleFunctionRepository: moduleFunctionMock);

        // Act
        var result = await service.CompareRolePermissionsAsync(roleId1, roleId2);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.OnlyInRole1.Count.ShouldBe(1);
        result.Data.OnlyInRole1[0].Id.ShouldBe(onlyRole1FuncId);
        result.Data.OnlyInRole1[0].Code.ShouldBe("role1.func");
        result.Data.OnlyInRole2.Count.ShouldBe(1);
        result.Data.OnlyInRole2[0].Id.ShouldBe(onlyRole2FuncId);
        result.Data.OnlyInRole2[0].Code.ShouldBe("role2.func");
        result.Data.Shared.Count.ShouldBe(1);
        result.Data.Shared[0].Id.ShouldBe(sharedFuncId);
        result.Data.Shared[0].Code.ShouldBe("shared.func");
    }

    [Fact]
    public async Task CompareRolePermissionsAsync_NoOverlap_ReturnsDisjointSets()
    {
        // Arrange
        var roleId1 = Guid.NewGuid();
        var roleId2 = Guid.NewGuid();
        var func1Id = Guid.NewGuid();
        var func2Id = Guid.NewGuid();
        var moduleId = Guid.NewGuid();

        var roleFunctions = new List<RoleFunction>
        {
            new() { RoleId = roleId1, FunctionId = func1Id, IsEnabled = true },
            new() { RoleId = roleId2, FunctionId = func2Id, IsEnabled = true }
        };

        var moduleFunctions = new List<ModuleFunction>
        {
            new() { Id = func1Id, Code = "func1", Name = "Function 1", ModuleId = moduleId, FunctionModule = new FunctionModule { Code = "mod1" } },
            new() { Id = func2Id, Code = "func2", Name = "Function 2", ModuleId = moduleId, FunctionModule = new FunctionModule { Code = "mod1" } }
        };

        var roleFunctionMock = CreateMockRoleFunctionRepository(roleFunctions);
        var moduleFunctionMock = CreateMockModuleFunctionRepository(moduleFunctions);

        var service = CreateService(
            roleFunctionRepository: roleFunctionMock,
            moduleFunctionRepository: moduleFunctionMock);

        // Act
        var result = await service.CompareRolePermissionsAsync(roleId1, roleId2);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.OnlyInRole1.Count.ShouldBe(1);
        result.Data.OnlyInRole1[0].Id.ShouldBe(func1Id);
        result.Data.OnlyInRole2.Count.ShouldBe(1);
        result.Data.OnlyInRole2[0].Id.ShouldBe(func2Id);
        result.Data.Shared.ShouldBeEmpty();
    }

    [Fact]
    public async Task CompareRolePermissionsAsync_DisabledFunctions_AreExcluded()
    {
        // Arrange
        var roleId1 = Guid.NewGuid();
        var roleId2 = Guid.NewGuid();
        var enabledFuncId = Guid.NewGuid();
        var disabledFuncId = Guid.NewGuid();

        var roleFunctions = new List<RoleFunction>
        {
            new() { RoleId = roleId1, FunctionId = enabledFuncId, IsEnabled = true },
            new() { RoleId = roleId1, FunctionId = disabledFuncId, IsEnabled = false } // 禁用的不应参与比较
        };

        var moduleFunctions = new List<ModuleFunction>
        {
            new() { Id = enabledFuncId, Code = "enabled.func", Name = "Enabled", FunctionModule = new FunctionModule { Code = "mod" } }
        };

        var roleFunctionMock = CreateMockRoleFunctionRepository(roleFunctions);
        var moduleFunctionMock = CreateMockModuleFunctionRepository(moduleFunctions);

        var service = CreateService(
            roleFunctionRepository: roleFunctionMock,
            moduleFunctionRepository: moduleFunctionMock);

        // Act
        var result = await service.CompareRolePermissionsAsync(roleId1, roleId2);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.OnlyInRole1.Count.ShouldBe(1);
        result.Data.OnlyInRole1[0].Id.ShouldBe(enabledFuncId);
        result.Data.OnlyInRole2.ShouldBeEmpty();
        result.Data.Shared.ShouldBeEmpty();
    }

    #endregion

    #region CloneRoleFunctionsAsync Tests

    [Fact]
    public async Task CloneRoleFunctionsAsync_SameRole_ReturnsFail()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var service = CreateService();

        // Act
        var result = await service.CloneRoleFunctionsAsync(roleId, roleId);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task CloneRoleFunctionsAsync_SourceEmpty_ReturnsZero()
    {
        // Arrange
        var sourceRoleId = Guid.NewGuid();
        var targetRoleId = Guid.NewGuid();

        var roleFunctionMock = CreateMockRoleFunctionRepository(new List<RoleFunction>());
        var service = CreateService(roleFunctionRepository: roleFunctionMock);

        // Act
        var result = await service.CloneRoleFunctionsAsync(sourceRoleId, targetRoleId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(0);
    }

    [Fact]
    public async Task CloneRoleFunctionsAsync_NormalClone_ReturnsNewCount()
    {
        // Arrange
        var sourceRoleId = Guid.NewGuid();
        var targetRoleId = Guid.NewGuid();
        var func1Id = Guid.NewGuid();
        var func2Id = Guid.NewGuid();

        var roleFunctions = new List<RoleFunction>
        {
            new() { RoleId = sourceRoleId, FunctionId = func1Id, IsEnabled = true },
            new() { RoleId = sourceRoleId, FunctionId = func2Id, IsEnabled = true }
        };

        var roleFunctionMock = CreateMockRoleFunctionRepository(roleFunctions);
        roleFunctionMock.Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<RoleFunction>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(roleFunctionRepository: roleFunctionMock);

        // Act
        var result = await service.CloneRoleFunctionsAsync(sourceRoleId, targetRoleId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(2);
        roleFunctionMock.Verify(r => r.InsertManyAsync(
            It.Is<IEnumerable<RoleFunction>>(items => items.Count() == 2 && items.All(rf => rf.RoleId == targetRoleId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CloneRoleFunctionsAsync_TargetHasOverlap_SkipsExisting()
    {
        // Arrange
        var sourceRoleId = Guid.NewGuid();
        var targetRoleId = Guid.NewGuid();
        var existingFuncId = Guid.NewGuid();
        var newFuncId = Guid.NewGuid();

        var roleFunctions = new List<RoleFunction>
        {
            // source role 有两个功能
            new() { RoleId = sourceRoleId, FunctionId = existingFuncId, IsEnabled = true },
            new() { RoleId = sourceRoleId, FunctionId = newFuncId, IsEnabled = true },
            // target role 已有其中一个
            new() { RoleId = targetRoleId, FunctionId = existingFuncId, IsEnabled = true }
        };

        var roleFunctionMock = CreateMockRoleFunctionRepository(roleFunctions);
        roleFunctionMock.Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<RoleFunction>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(roleFunctionRepository: roleFunctionMock);

        // Act
        var result = await service.CloneRoleFunctionsAsync(sourceRoleId, targetRoleId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(1); // 只新增1个，跳过已有的
        roleFunctionMock.Verify(r => r.InsertManyAsync(
            It.Is<IEnumerable<RoleFunction>>(items => items.Count() == 1 && items.First().FunctionId == newFuncId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CloneRoleFunctionsAsync_AllExistInTarget_ReturnsZero()
    {
        // Arrange
        var sourceRoleId = Guid.NewGuid();
        var targetRoleId = Guid.NewGuid();
        var funcId = Guid.NewGuid();

        var roleFunctions = new List<RoleFunction>
        {
            new() { RoleId = sourceRoleId, FunctionId = funcId, IsEnabled = true },
            new() { RoleId = targetRoleId, FunctionId = funcId, IsEnabled = true }
        };

        var roleFunctionMock = CreateMockRoleFunctionRepository(roleFunctions);
        var service = CreateService(roleFunctionRepository: roleFunctionMock);

        // Act
        var result = await service.CloneRoleFunctionsAsync(sourceRoleId, targetRoleId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(0);
        roleFunctionMock.Verify(r => r.InsertManyAsync(It.IsAny<IEnumerable<RoleFunction>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region ExportRolePermissionsAsync Tests

    [Fact]
    public async Task ExportRolePermissionsAsync_RoleWithFunctions_ReturnsCodes()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var func1Id = Guid.NewGuid();
        var func2Id = Guid.NewGuid();

        var roleFunctions = new List<RoleFunction>
        {
            new() { RoleId = roleId, FunctionId = func1Id, IsEnabled = true },
            new() { RoleId = roleId, FunctionId = func2Id, IsEnabled = true }
        };

        var moduleFunctions = new List<ModuleFunction>
        {
            new() { Id = func1Id, Code = "users.create", Name = "Create User", IsEnabled = true },
            new() { Id = func2Id, Code = "users.delete", Name = "Delete User", IsEnabled = true }
        };

        var roleFunctionMock = CreateMockRoleFunctionRepository(roleFunctions);
        var moduleFunctionMock = CreateMockModuleFunctionRepository(moduleFunctions);

        var service = CreateService(
            roleFunctionRepository: roleFunctionMock,
            moduleFunctionRepository: moduleFunctionMock);

        // Act
        var result = await service.ExportRolePermissionsAsync(roleId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Version.ShouldBe("1.0");
        result.Data.SourceRoleId.ShouldBe(roleId);
        result.Data.FunctionCodes.Count.ShouldBe(2);
        result.Data.FunctionCodes.ShouldContain("users.create");
        result.Data.FunctionCodes.ShouldContain("users.delete");
        result.Data.ExportedAt.ShouldNotBe(default);
    }

    [Fact]
    public async Task ExportRolePermissionsAsync_RoleWithNoFunctions_ReturnsEmptyCodes()
    {
        // Arrange
        var roleId = Guid.NewGuid();

        var roleFunctionMock = CreateMockRoleFunctionRepository(new List<RoleFunction>());
        var moduleFunctionMock = CreateMockModuleFunctionRepository(new List<ModuleFunction>());

        var service = CreateService(
            roleFunctionRepository: roleFunctionMock,
            moduleFunctionRepository: moduleFunctionMock);

        // Act
        var result = await service.ExportRolePermissionsAsync(roleId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.FunctionCodes.ShouldBeEmpty();
        result.Data.SourceRoleId.ShouldBe(roleId);
    }

    [Fact]
    public async Task ExportRolePermissionsAsync_DisabledFunctions_AreExcluded()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var enabledFuncId = Guid.NewGuid();
        var disabledFuncId = Guid.NewGuid();

        var roleFunctions = new List<RoleFunction>
        {
            new() { RoleId = roleId, FunctionId = enabledFuncId, IsEnabled = true },
            new() { RoleId = roleId, FunctionId = disabledFuncId, IsEnabled = false }
        };

        var moduleFunctions = new List<ModuleFunction>
        {
            new() { Id = enabledFuncId, Code = "enabled.func", Name = "Enabled", IsEnabled = true }
        };

        var roleFunctionMock = CreateMockRoleFunctionRepository(roleFunctions);
        var moduleFunctionMock = CreateMockModuleFunctionRepository(moduleFunctions);

        var service = CreateService(
            roleFunctionRepository: roleFunctionMock,
            moduleFunctionRepository: moduleFunctionMock);

        // Act
        var result = await service.ExportRolePermissionsAsync(roleId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.FunctionCodes.Count.ShouldBe(1);
        result.Data.FunctionCodes.ShouldContain("enabled.func");
    }

    #endregion

    #region ImportRolePermissionsAsync Tests

    [Fact]
    public async Task ImportRolePermissionsAsync_NormalImport_ReturnsImportedCount()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var func1Id = Guid.NewGuid();
        var func2Id = Guid.NewGuid();

        var moduleFunctions = new List<ModuleFunction>
        {
            new() { Id = func1Id, Code = "users.create", Name = "Create User", IsEnabled = true },
            new() { Id = func2Id, Code = "users.delete", Name = "Delete User", IsEnabled = true }
        };

        // 角色当前没有任何功能
        var roleFunctions = new List<RoleFunction>();

        var roleFunctionMock = CreateMockRoleFunctionRepository(roleFunctions);
        roleFunctionMock.Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<RoleFunction>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var moduleFunctionMock = CreateMockModuleFunctionRepository(moduleFunctions);

        var service = CreateService(
            roleFunctionRepository: roleFunctionMock,
            moduleFunctionRepository: moduleFunctionMock);

        var importData = new RolePermissionExportDto
        {
            FunctionCodes = new List<string> { "users.create", "users.delete" }
        };

        // Act
        var result = await service.ImportRolePermissionsAsync(roleId, importData);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Imported.ShouldBe(2);
        result.Data.Skipped.ShouldBe(0);
        result.Data.NotFound.ShouldBeEmpty();
        roleFunctionMock.Verify(r => r.InsertManyAsync(
            It.Is<IEnumerable<RoleFunction>>(items => items.Count() == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportRolePermissionsAsync_EmptyImport_ReturnsZeroCounts()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var service = CreateService();

        var importData = new RolePermissionExportDto
        {
            FunctionCodes = new List<string>()
        };

        // Act
        var result = await service.ImportRolePermissionsAsync(roleId, importData);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Imported.ShouldBe(0);
        result.Data.Skipped.ShouldBe(0);
        result.Data.NotFound.ShouldBeEmpty();
    }

    [Fact]
    public async Task ImportRolePermissionsAsync_CodesNotFound_ReportsNotFound()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var func1Id = Guid.NewGuid();

        // 系统中只有1个功能
        var moduleFunctions = new List<ModuleFunction>
        {
            new() { Id = func1Id, Code = "users.create", Name = "Create User", IsEnabled = true }
        };

        var roleFunctions = new List<RoleFunction>();

        var roleFunctionMock = CreateMockRoleFunctionRepository(roleFunctions);
        roleFunctionMock.Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<RoleFunction>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var moduleFunctionMock = CreateMockModuleFunctionRepository(moduleFunctions);

        var service = CreateService(
            roleFunctionRepository: roleFunctionMock,
            moduleFunctionRepository: moduleFunctionMock);

        var importData = new RolePermissionExportDto
        {
            FunctionCodes = new List<string> { "users.create", "nonexistent.code", "another.missing" }
        };

        // Act
        var result = await service.ImportRolePermissionsAsync(roleId, importData);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Imported.ShouldBe(1);
        result.Data.Skipped.ShouldBe(0);
        result.Data.NotFound.Count.ShouldBe(2);
        result.Data.NotFound.ShouldContain("nonexistent.code");
        result.Data.NotFound.ShouldContain("another.missing");
    }

    [Fact]
    public async Task ImportRolePermissionsAsync_CodesAlreadyExisting_ReportsSkipped()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var func1Id = Guid.NewGuid();
        var func2Id = Guid.NewGuid();

        var moduleFunctions = new List<ModuleFunction>
        {
            new() { Id = func1Id, Code = "users.create", Name = "Create User", IsEnabled = true },
            new() { Id = func2Id, Code = "users.delete", Name = "Delete User", IsEnabled = true }
        };

        // 角色已有 func1 的分配
        var roleFunctions = new List<RoleFunction>
        {
            new() { RoleId = roleId, FunctionId = func1Id, IsEnabled = true }
        };

        var roleFunctionMock = CreateMockRoleFunctionRepository(roleFunctions);
        roleFunctionMock.Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<RoleFunction>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var moduleFunctionMock = CreateMockModuleFunctionRepository(moduleFunctions);

        var service = CreateService(
            roleFunctionRepository: roleFunctionMock,
            moduleFunctionRepository: moduleFunctionMock);

        var importData = new RolePermissionExportDto
        {
            FunctionCodes = new List<string> { "users.create", "users.delete" }
        };

        // Act
        var result = await service.ImportRolePermissionsAsync(roleId, importData);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Imported.ShouldBe(1); // 只有 users.delete 是新的
        result.Data.Skipped.ShouldBe(1); // users.create 已存在
        result.Data.NotFound.ShouldBeEmpty();
    }

    [Fact]
    public async Task ImportRolePermissionsAsync_AllCodesNotFound_ImportsNothing()
    {
        // Arrange
        var roleId = Guid.NewGuid();

        // 系统中没有匹配的功能
        var moduleFunctions = new List<ModuleFunction>();
        var roleFunctions = new List<RoleFunction>();

        var roleFunctionMock = CreateMockRoleFunctionRepository(roleFunctions);
        var moduleFunctionMock = CreateMockModuleFunctionRepository(moduleFunctions);

        var service = CreateService(
            roleFunctionRepository: roleFunctionMock,
            moduleFunctionRepository: moduleFunctionMock);

        var importData = new RolePermissionExportDto
        {
            FunctionCodes = new List<string> { "missing.code1", "missing.code2" }
        };

        // Act
        var result = await service.ImportRolePermissionsAsync(roleId, importData);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Imported.ShouldBe(0);
        result.Data.Skipped.ShouldBe(0);
        result.Data.NotFound.Count.ShouldBe(2);
        roleFunctionMock.Verify(r => r.InsertManyAsync(It.IsAny<IEnumerable<RoleFunction>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportRolePermissionsAsync_DisabledFunctions_AreNotResolved()
    {
        // Arrange: 导入时只匹配 IsEnabled=true 的功能
        var roleId = Guid.NewGuid();
        var enabledFuncId = Guid.NewGuid();
        var disabledFuncId = Guid.NewGuid();

        var moduleFunctions = new List<ModuleFunction>
        {
            new() { Id = enabledFuncId, Code = "enabled.func", Name = "Enabled", IsEnabled = true },
            new() { Id = disabledFuncId, Code = "disabled.func", Name = "Disabled", IsEnabled = false }
        };

        var roleFunctions = new List<RoleFunction>();

        var roleFunctionMock = CreateMockRoleFunctionRepository(roleFunctions);
        roleFunctionMock.Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<RoleFunction>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var moduleFunctionMock = CreateMockModuleFunctionRepository(moduleFunctions);

        var service = CreateService(
            roleFunctionRepository: roleFunctionMock,
            moduleFunctionRepository: moduleFunctionMock);

        var importData = new RolePermissionExportDto
        {
            FunctionCodes = new List<string> { "enabled.func", "disabled.func" }
        };

        // Act
        var result = await service.ImportRolePermissionsAsync(roleId, importData);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.Imported.ShouldBe(1); // 只有 enabled.func 被导入
        result.Data.NotFound.Count.ShouldBe(1);
        result.Data.NotFound.ShouldContain("disabled.func"); // 禁用的视为未找到
    }

    #endregion

    #region DTO Tests (New DTOs)

    [Fact]
    public void PermissionComparisonDto_DefaultValues()
    {
        // Act
        var dto = new PermissionComparisonDto();

        // Assert
        dto.RoleId1.ShouldBe(Guid.Empty);
        dto.RoleId2.ShouldBe(Guid.Empty);
        dto.OnlyInRole1.ShouldNotBeNull();
        dto.OnlyInRole1.ShouldBeEmpty();
        dto.OnlyInRole2.ShouldNotBeNull();
        dto.OnlyInRole2.ShouldBeEmpty();
        dto.Shared.ShouldNotBeNull();
        dto.Shared.ShouldBeEmpty();
    }

    [Fact]
    public void FunctionSummaryDto_DefaultValues()
    {
        // Act
        var dto = new FunctionSummaryDto();

        // Assert
        dto.Id.ShouldBe(Guid.Empty);
        dto.Code.ShouldBe(string.Empty);
        dto.Name.ShouldBe(string.Empty);
        dto.ModuleCode.ShouldBeNull();
    }

    [Fact]
    public void RolePermissionExportDto_DefaultValues()
    {
        // Act
        var dto = new RolePermissionExportDto();

        // Assert
        dto.Version.ShouldBe("1.0");
        dto.SourceRoleId.ShouldBeNull();
        dto.FunctionCodes.ShouldNotBeNull();
        dto.FunctionCodes.ShouldBeEmpty();
    }

    [Fact]
    public void PermissionImportResultDto_DefaultValues()
    {
        // Act
        var dto = new PermissionImportResultDto();

        // Assert
        dto.Imported.ShouldBe(0);
        dto.Skipped.ShouldBe(0);
        dto.NotFound.ShouldNotBeNull();
        dto.NotFound.ShouldBeEmpty();
    }

    #endregion

    #region Helper Methods

    private FunctionAuthorizationService CreateService(
        Mock<IRepository<FunctionModule, Guid>>? moduleRepository = null,
        Mock<IRepository<ModuleFunction, Guid>>? moduleFunctionRepository = null,
        Mock<IRepository<ModuleUser, Guid>>? moduleUserRepository = null,
        Mock<IRepository<ModuleRole, Guid>>? moduleRoleRepository = null,
        Mock<IRepository<RoleFunction, Guid>>? roleFunctionRepository = null,
        IUserRoleService? userRoleService = null)
    {
        return new FunctionAuthorizationService(
            moduleRepository?.Object ?? new Mock<IRepository<FunctionModule, Guid>>().Object,
            moduleFunctionRepository?.Object ?? CreateMockModuleFunctionRepository(new List<ModuleFunction>()).Object,
            moduleUserRepository?.Object ?? new Mock<IRepository<ModuleUser, Guid>>().Object,
            moduleRoleRepository?.Object ?? new Mock<IRepository<ModuleRole, Guid>>().Object,
            roleFunctionRepository?.Object ?? CreateMockRoleFunctionRepository(new List<RoleFunction>()).Object,
            _serviceProviderMock.Object,
            userRoleService
        );
    }

    /// <summary>
    /// 创建支持 IQueryable 的 RoleFunction Repository Mock
    /// 使用 MockQueryable 来支持 LINQ 扩展方法（Where/Select/ToListAsync）
    /// </summary>
    private static Mock<IRepository<RoleFunction, Guid>> CreateMockRoleFunctionRepository(List<RoleFunction> data)
    {
        var mock = new Mock<IRepository<RoleFunction, Guid>>();
        var mockQueryable = data.BuildMock();

        mock.As<IQueryable<RoleFunction>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        mock.As<IQueryable<RoleFunction>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        mock.As<IQueryable<RoleFunction>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        mock.As<IQueryable<RoleFunction>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());

        return mock;
    }

    /// <summary>
    /// 创建支持 IQueryable 的 ModuleFunction Repository Mock
    /// 使用 MockQueryable 来支持 LINQ 扩展方法（Where/Select/ToListAsync/Include）
    /// </summary>
    private static Mock<IRepository<ModuleFunction, Guid>> CreateMockModuleFunctionRepository(List<ModuleFunction> data)
    {
        var mock = new Mock<IRepository<ModuleFunction, Guid>>();
        var mockQueryable = data.BuildMock();

        mock.As<IQueryable<ModuleFunction>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        mock.As<IQueryable<ModuleFunction>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        mock.As<IQueryable<ModuleFunction>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        mock.As<IQueryable<ModuleFunction>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());

        return mock;
    }

    #endregion
}
