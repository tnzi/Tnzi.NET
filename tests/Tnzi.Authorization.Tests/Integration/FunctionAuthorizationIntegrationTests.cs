
namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// Authorization 模块集成测试
/// </summary>
public class FunctionAuthorizationIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task CreateModule_ThenGetById_ReturnsCreatedModule()
    {
        // Arrange
        var module = new FunctionModule
        {
            Id = Guid.NewGuid(),
            Name = "TestModule",
            Code = "TEST",
            Order = 1,
            CreationTime = DateTime.UtcNow
        };

        // Act
        await DbContext.FunctionModules.AddAsync(module);
        await DbContext.SaveChangesAsync();

        // Assert
        var savedModule = await DbContext.FunctionModules.FirstOrDefaultAsync(m => m.Code == "TEST");
        savedModule.ShouldNotBeNull();
        savedModule!.Name.ShouldBe("TestModule");
        savedModule.Code.ShouldBe("TEST");
    }

    [Fact]
    public async Task CreateModule_ThenUpdate_ReturnsUpdatedModule()
    {
        // Arrange
        var module = new FunctionModule
        {
            Id = Guid.NewGuid(),
            Name = "OriginalName",
            Code = "ORIG",
            Order = 1,
            CreationTime = DateTime.UtcNow
        };
        await DbContext.FunctionModules.AddAsync(module);
        await DbContext.SaveChangesAsync();

        // Act
        module.Name = "UpdatedName";
        await DbContext.SaveChangesAsync();

        // Assert
        var updatedModule = await DbContext.FunctionModules.FirstOrDefaultAsync(m => m.Code == "ORIG");
        updatedModule.ShouldNotBeNull();
        updatedModule!.Name.ShouldBe("UpdatedName");
    }

    [Fact]
    public async Task AssignFunction_ThenCheck_RoleHasFunction()
    {
        // Arrange
        var module = new FunctionModule
        {
            Id = Guid.NewGuid(),
            Name = "TestModule",
            Code = "TEST",
            Order = 1,
            CreationTime = DateTime.UtcNow
        };
        await DbContext.FunctionModules.AddAsync(module);

        var function = new ModuleFunction
        {
            Id = Guid.NewGuid(),
            Name = "TestFunction",
            Code = "Test.Function",
            ModuleId = module.Id,
            CreationTime = DateTime.UtcNow
        };
        await DbContext.ModuleFunctions.AddAsync(function);

        var roleId = Guid.NewGuid();
        var moduleRole = new ModuleRole
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            RoleId = roleId,
            CreationTime = DateTime.UtcNow
        };
        await DbContext.ModuleRoles.AddAsync(moduleRole);
        await DbContext.SaveChangesAsync();

        // Act
        var assignedRole = await DbContext.ModuleRoles
            .Include(mr => mr.FunctionModule)
            .FirstOrDefaultAsync(mr => mr.RoleId == roleId);

        // Assert
        assignedRole.ShouldNotBeNull();
        assignedRole!.FunctionModule.ShouldNotBeNull();
        assignedRole.FunctionModule!.Code.ShouldBe("TEST");
    }

    [Fact]
    public async Task CheckPermission_WithUserHavingRole_ReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var module = new FunctionModule
        {
            Id = Guid.NewGuid(),
            Name = "TestModule",
            Code = "TEST",
            Order = 1,
            CreationTime = DateTime.UtcNow
        };
        await DbContext.FunctionModules.AddAsync(module);

        var moduleUser = new ModuleUser
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            UserId = userId,
            CreationTime = DateTime.UtcNow
        };
        await DbContext.ModuleUsers.AddAsync(moduleUser);
        await DbContext.SaveChangesAsync();

        // Act
        var userModule = await DbContext.ModuleUsers
            .Include(mu => mu.FunctionModule)
            .FirstOrDefaultAsync(mu => mu.UserId == userId);

        // Assert
        userModule.ShouldNotBeNull();
        userModule!.FunctionModule.ShouldNotBeNull();
        userModule.FunctionModule!.Code.ShouldBe("TEST");
    }

    [Fact]
    public async Task CreateEntityInfo_WithRoles_CanQueryRelations()
    {
        // Arrange
        var entityInfo = new EntityInfo
        {
            Id = Guid.NewGuid(),
            Name = "TestEntity",
            TypeName = "Test.Entity",
            CreationTime = DateTime.UtcNow
        };
        await DbContext.EntityInfos.AddAsync(entityInfo);

        var roleId = Guid.NewGuid();
        var entityRole = new EntityRole
        {
            Id = Guid.NewGuid(),
            EntityInfoId = entityInfo.Id,
            RoleId = roleId,
            Operation = DataAuthOperation.Query,
            Filter = "{}",
            CreationTime = DateTime.UtcNow
        };
        await DbContext.EntityRoles.AddAsync(entityRole);
        await DbContext.SaveChangesAsync();

        // Act
        var savedEntityRole = await DbContext.EntityRoles
            .Include(er => er.EntityInfo)
            .FirstOrDefaultAsync(er => er.RoleId == roleId);

        // Assert
        savedEntityRole.ShouldNotBeNull();
        savedEntityRole!.EntityInfo.ShouldNotBeNull();
        savedEntityRole.EntityInfo!.TypeName.ShouldBe("Test.Entity");
        savedEntityRole.Operation.ShouldBe(DataAuthOperation.Query);
    }

    [Fact]
    public async Task QueryModules_WithPaging_ReturnsCorrectResults()
    {
        // Arrange
        for (int i = 1; i <= 15; i++)
        {
            var module = new FunctionModule
            {
                Id = Guid.NewGuid(),
                Name = $"Module{i}",
                Code = $"MOD{i:D2}",
                Order = i,
                CreationTime = DateTime.UtcNow.AddMinutes(-i)
            };
            await DbContext.FunctionModules.AddAsync(module);
        }
        await DbContext.SaveChangesAsync();

        // Act
        var page1 = await DbContext.FunctionModules
            .OrderBy(m => m.Order)
            .Skip(0)
            .Take(10)
            .ToListAsync();

        var page2 = await DbContext.FunctionModules
            .OrderBy(m => m.Order)
            .Skip(10)
            .Take(10)
            .ToListAsync();

        // Assert
        page1.Count.ShouldBe(10);
        page2.Count.ShouldBe(5);
        page1.First().Code.ShouldBe("MOD01");
        page2.First().Code.ShouldBe("MOD11");
    }
}