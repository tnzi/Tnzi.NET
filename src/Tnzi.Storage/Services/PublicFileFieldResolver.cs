namespace Tnzi.Storage.Services;

/// <summary>
/// 默认实现：从 <see cref="IEntityManager"/> 已注册的实体类型里反射出所有
/// <c>[FileField(Public = true)]</c> 属性。
///
/// 只看**实体**（不看 DTO）：文件引用表里的 EntityType/FieldName 由
/// <c>FileReferenceChangeTracker</c> 从 EF ChangeTracker 的实体条目写入，
/// DTO 上的同名特性不产生引用行，纳进来只会造出永远匹配不上的条目。
/// </summary>
public class PublicFileFieldResolver : IPublicFileFieldResolver
{
    private readonly IEntityManager _entityManager;
    private readonly Lazy<IReadOnlyCollection<PublicFileField>> _fields;

    public PublicFileFieldResolver(IEntityManager entityManager)
    {
        _entityManager = Check.NotNull(entityManager);
        _fields = new Lazy<IReadOnlyCollection<PublicFileField>>(Scan, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PublicFileField> GetPublicFileFields() => _fields.Value;

    private IReadOnlyCollection<PublicFileField> Scan()
    {
        var result = new HashSet<PublicFileField>();

        foreach (var entityType in _entityManager.GetAllEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var attribute = property.GetCustomAttribute<FileFieldAttribute>();
                if (attribute is { Public: true })
                {
                    result.Add(new PublicFileField(entityType.Name, property.Name));
                }
            }
        }

        return result;
    }
}
