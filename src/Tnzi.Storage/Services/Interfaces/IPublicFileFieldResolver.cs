namespace Tnzi.Storage.Services;

/// <summary>
/// 一个被声明为公开的文件字段：实体类型名 + 属性名。
/// 与 <c>FileReference.EntityType</c> / <c>FileReference.FieldName</c> 的写法一致
/// （实体类型用短名 <c>Type.Name</c>，由 <c>FileReferenceChangeTracker</c> 写入）。
/// </summary>
public readonly record struct PublicFileField(string EntityType, string FieldName);

/// <summary>
/// 找出所有标了 <c>[FileField(Public = true)]</c> 的实体属性。
///
/// 存在的理由是**回填**：字段声明只对之后写入的引用生效，早已躺在库里的头像不会
/// 自己重存一遍。有了这份清单，就能反过来从文件引用表推出"哪些既有文件本应是公开的"。
/// </summary>
public interface IPublicFileFieldResolver
{
    /// <summary>
    /// 返回全部声明为公开的文件字段（结果缓存，反射只做一次）。
    /// </summary>
    IReadOnlyCollection<PublicFileField> GetPublicFileFields();
}
