
namespace Tnzi.Tests.Reflection;

public class TypeCacheTests
{
    [Fact]
    public void GetOrAdd_ShouldCacheTypeMetadata()
    {
        // Arrange
        TypeCache.Clear();

        // Act
        var metadata1 = TypeCache.GetOrAdd<TestClass>();
        var metadata2 = TypeCache.GetOrAdd<TestClass>();

        // Assert
        Assert.Same(metadata1, metadata2);
        Assert.Equal(1, TypeCache.Count);
    }

    [Fact]
    public void TypeMetadata_ShouldContainProperties()
    {
        // Arrange & Act
        var metadata = TypeCache.GetOrAdd<TestClass>();

        // Assert
        Assert.True(metadata.Properties.Length > 0);
        Assert.NotNull(metadata.GetProperty("Name"));
    }

    [Fact]
    public void TypeMetadata_ShouldDetectInterfaces()
    {
        // Arrange & Act
        var metadata = TypeCache.GetOrAdd<TestClass>();

        // Assert
        Assert.True(metadata.ImplementsInterface<ITestInterface>());
    }

    [Fact]
    public void TypeMetadata_ShouldDetectAttributes()
    {
        // Arrange & Act
        var metadata = TypeCache.GetOrAdd<TestClass>();

        // Assert
        Assert.True(metadata.HasAttribute<TestAttribute>());
    }

    [Fact]
    public void AssemblyTypeScanner_ShouldFindImplementations()
    {
        // Arrange
        AssemblyTypeScanner.ClearCache();

        // Act
        var types = AssemblyTypeScanner.ScanTypes<ITestInterface>(GetType().Assembly);

        // Assert
        Assert.Contains(types, t => t == typeof(TestClass));
    }

    [Fact]
    public void Remove_ShouldClearSpecificType()
    {
        // Arrange
        TypeCache.Clear();
        TypeCache.GetOrAdd<TestClass>();
        Assert.Equal(1, TypeCache.Count);

        // Act
        var result = TypeCache.Remove(typeof(TestClass));

        // Assert
        Assert.True(result);
        Assert.Equal(0, TypeCache.Count);
    }

    // Test fixtures
    public interface ITestInterface { }

    [AttributeUsage(AttributeTargets.Class)]
    public class TestAttribute : Attribute { }

    [Test]
    public class TestClass : ITestInterface
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }
}