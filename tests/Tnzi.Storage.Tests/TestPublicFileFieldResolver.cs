namespace Tnzi.Storage.Tests;

/// <summary>
/// 测试用的公开文件字段清单，内容由构造参数固定，不做反射。
///
/// 真实实现要靠 <c>IEntityManager</c> 枚举已注册实体，纯单元测试里既没有 DbContext
/// 也没有实体注册表；绝大多数既有用例也不关心回填，所以默认 <see cref="Empty"/> 空清单。
/// 回填行为本身由 <c>PublicFileFieldBackfillTests</c> 用显式清单覆盖。
/// </summary>
public sealed class TestPublicFileFieldResolver : IPublicFileFieldResolver
{
    private readonly IReadOnlyCollection<PublicFileField> _fields;

    public TestPublicFileFieldResolver(params PublicFileField[] fields)
    {
        _fields = fields;
    }

    /// <summary>无任何公开字段声明，用于与回填无关的用例。</summary>
    public static TestPublicFileFieldResolver Empty() => new();

    /// <summary>按 (实体类型名, 属性名) 声明公开字段。</summary>
    public static TestPublicFileFieldResolver With(params (string EntityType, string FieldName)[] fields)
        => new(fields.Select(f => new PublicFileField(f.EntityType, f.FieldName)).ToArray());

    public IReadOnlyCollection<PublicFileField> GetPublicFileFields() => _fields;
}
