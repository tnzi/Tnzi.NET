using Tnzi.MultiTenancy;

namespace Tnzi.Tests.Caching;

/// <summary>
/// CacheKeyGenerator 多租户缓存键隔离测试
/// </summary>
public class CacheKeyGeneratorTenantTests
{
    // 辅助方法：创建带租户上下文的 CacheKeyGenerator
    private static CacheKeyGenerator CreateGeneratorWithTenant(Guid tenantId)
    {
        var currentTenant = new Mock<ICurrentTenant>();
        currentTenant.Setup(t => t.Id).Returns(tenantId);

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(sp => sp.GetService(typeof(ICurrentTenant)))
            .Returns(currentTenant.Object);

        return new CacheKeyGenerator(serviceProvider.Object);
    }

    // 辅助方法：创建无租户上下文的 CacheKeyGenerator
    private static CacheKeyGenerator CreateGeneratorWithoutTenant()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(sp => sp.GetService(typeof(ICurrentTenant)))
            .Returns((object?)null);

        return new CacheKeyGenerator(serviceProvider.Object);
    }

    // ===== Generate =====

    [Fact]
    public void Generate_WithTenantActive_KeyStartsWithTenantPrefix()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var generator = CreateGeneratorWithTenant(tenantId);
        var expectedPrefix = $"t:{tenantId:N}:";

        // Act
        var key = generator.Generate("products", "page1");

        // Assert
        Assert.StartsWith(expectedPrefix, key);
    }

    [Fact]
    public void Generate_WithTenantActive_KeyContainsPrefixAndParams()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var generator = CreateGeneratorWithTenant(tenantId);

        // Act
        var key = generator.Generate("products", "page1");

        // Assert - 格式：t:{tenantId:N}:products:page1
        Assert.Equal($"t:{tenantId:N}:products:page1", key);
    }

    [Fact]
    public void Generate_WithoutTenant_NoTenantPrefix()
    {
        // Arrange
        var generator = CreateGeneratorWithoutTenant();

        // Act
        var key = generator.Generate("products", "page1");

        // Assert - 不含租户前缀
        Assert.DoesNotContain("t:", key);
        Assert.Equal("products:page1", key);
    }

    // ===== GenerateWithSeparator =====

    [Fact]
    public void GenerateWithSeparator_WithTenantActive_IncludesTenantPrefix()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var generator = CreateGeneratorWithTenant(tenantId);

        // Act
        var key = generator.GenerateWithSeparator(":", "orders", "2026");

        // Assert - 格式：t:{tenantId:N}:orders:2026
        Assert.StartsWith($"t:{tenantId:N}:", key);
        Assert.Contains("orders", key);
        Assert.Contains("2026", key);
    }

    [Fact]
    public void GenerateWithSeparator_WithoutTenant_NoTenantSegment()
    {
        // Arrange
        var generator = CreateGeneratorWithoutTenant();

        // Act
        var key = generator.GenerateWithSeparator(":", "orders", "2026");

        // Assert
        Assert.Equal("orders:2026", key);
    }

    // ===== GenerateForMethod =====

    [Fact]
    public void GenerateForMethod_WithTenantActive_IncludesTenantPrefix()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var generator = CreateGeneratorWithTenant(tenantId);

        // Act
        var key = generator.GenerateForMethod("UserService", "GetById", Guid.Empty);

        // Assert
        Assert.StartsWith($"t:{tenantId:N}:", key);
        Assert.Contains("UserService", key);
        Assert.Contains("GetById", key);
    }

    [Fact]
    public void GenerateForMethod_WithoutTenant_NoTenantPrefix()
    {
        // Arrange
        var generator = CreateGeneratorWithoutTenant();

        // Act
        var key = generator.GenerateForMethod("UserService", "GetById", Guid.Empty);

        // Assert
        Assert.StartsWith("UserService:", key);
        Assert.DoesNotContain("t:", key);
    }

    // ===== GenerateForQuery<T> =====

    [Fact]
    public void GenerateForQuery_WithTenantActive_IncludesTenantSegment()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var generator = CreateGeneratorWithTenant(tenantId);

        // Act
        var key = generator.GenerateForQuery<string>("x => x.IsActive == true");

        // Assert - 格式：t:{tenantId:N}:cache:query:{typeName}:{hash}
        Assert.StartsWith($"t:{tenantId:N}:", key);
        Assert.Contains("cache:query:", key);
    }

    [Fact]
    public void GenerateForQuery_WithoutTenant_NoTenantSegment()
    {
        // Arrange
        var generator = CreateGeneratorWithoutTenant();

        // Act
        var key = generator.GenerateForQuery<string>("x => x.IsActive == true");

        // Assert - 格式：cache:query:{typeName}:{hash}
        Assert.StartsWith("cache:query:", key);
        Assert.DoesNotContain("t:", key);
    }

    // ===== 不同租户产生不同缓存键 =====

    [Fact]
    public void Generate_DifferentTenants_ProduceDifferentKeys()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var generatorA = CreateGeneratorWithTenant(tenantA);
        var generatorB = CreateGeneratorWithTenant(tenantB);

        // Act
        var keyA = generatorA.Generate("products");
        var keyB = generatorB.Generate("products");

        // Assert - 相同输入，不同租户 → 不同键
        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void GenerateForQuery_DifferentTenants_ProduceDifferentKeys()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var generatorA = CreateGeneratorWithTenant(tenantA);
        var generatorB = CreateGeneratorWithTenant(tenantB);
        const string expr = "x => x.Status == 1";

        // Act
        var keyA = generatorA.GenerateForQuery<int>(expr);
        var keyB = generatorB.GenerateForQuery<int>(expr);

        // Assert
        Assert.NotEqual(keyA, keyB);
    }

    // ===== CacheKeys.WithTenant =====

    [Fact]
    public void CacheKeys_WithTenant_WhenTenantIdProvided_AddsTenantPrefix()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        const string originalKey = "user:123";

        // Act
        var result = CacheKeys.WithTenant(originalKey, tenantId);

        // Assert - 格式：t:{tenantId:N}:{originalKey}
        Assert.Equal($"t:{tenantId:N}:{originalKey}", result);
    }

    [Fact]
    public void CacheKeys_WithTenant_WhenTenantIdIsNull_ReturnsOriginalKey()
    {
        // Arrange
        const string originalKey = "user:123";

        // Act
        var result = CacheKeys.WithTenant(originalKey, null);

        // Assert - 无租户 ID 时原样返回
        Assert.Equal(originalKey, result);
    }
}
