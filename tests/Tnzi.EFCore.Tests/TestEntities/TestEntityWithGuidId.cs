
namespace Tnzi.EFCore.Tests.TestEntities;

/// <summary>
/// 测试实体 - Guid 类型 Id
/// </summary>
public class TestEntityWithGuidId : EntityBase<Guid>
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 测试实体 - 可空 Guid 类型 Id
/// </summary>
public class TestEntityWithNullableGuidId : EntityBase<Guid?>
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 测试实体 - long 类型 Id
/// </summary>
public class TestEntityWithLongId : EntityBase<long>
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 测试实体 - int 类型 Id（用于测试不自动生成的情况）
/// </summary>
public class TestEntityWithIntId : EntityBase<int>
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 测试实体 - 没有 Id 属性（用于测试边界情况）
/// </summary>
public class TestEntityWithoutId : IEntity
{
    public string Name { get; set; } = string.Empty;

    public object[] GetKeys()
    {
        return Array.Empty<object>();
    }
}

/// <summary>
/// 测试实体 - 只读 Id 属性（用于测试边界情况）
/// 注意：不实现 IEntity<Guid>，因为只读属性无法满足接口要求
/// </summary>
public class TestEntityWithReadOnlyId : IEntity
{
    public Guid Id { get; } = Guid.Empty; // 只读属性

    public string Name { get; set; } = string.Empty;

    public object[] GetKeys()
    {
        return new object[] { Id };
    }
}