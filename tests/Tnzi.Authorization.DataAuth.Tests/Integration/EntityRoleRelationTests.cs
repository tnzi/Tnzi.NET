namespace Tnzi.Authorization.DataAuth.Tests.Integration;

/// <summary>
/// 两张表的往返与关系映射（拆分前住在 <c>Tnzi.Authorization.Tests</c>）。
/// </summary>
public class EntityRoleRelationTests : IntegrationTestBase
{
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
}
