using System.Collections;

namespace Tnzi.Data.Filtering;

/// <summary>
/// 动态过滤的字段准入策略：一条来自请求的 <see cref="FilterRule.Field"/> 允许落在实体的哪些属性上。
/// </summary>
/// <remarks>
/// <see cref="FilterExpressionBuilder"/> 对字段路径逐段反射，没有策略时任何公共属性（含导航到别的实体）都能进谓词。
/// 请求来源的过滤器必须经策略过滤：持有列表 <c>.view</c> 码的调用方否则能按从不投影的列
/// （口令哈希、安全戳、令牌、薪资）用 <c>StartsWith</c> / <c>GreaterThan</c> 逐字符二分，行级范围谓词只是 AND 上去，
/// 挡不住按存在性回答的探测。
/// <para>
/// <see cref="ProjectedBy"/> 是请求数据的默认形态（<c>CrudAppService</c> 按返回 DTO 生成）：只允许 DTO 上同名的根标量列；
/// <see cref="Request"/> 放行根实体的全部公共标量属性、禁止含 <c>.</c> 的导航路径，根列上没有秘密时才够用；
/// <see cref="Unrestricted"/> 给服务端自己构造的过滤器（数据授权规则、内部查询）。
/// 需要按导航筛选的端点显式开 <see cref="AllowNavigation"/> 并限 <see cref="MaxDepth"/>，
/// 或用 <see cref="AllowOnly"/> 点名允许的路径。
/// </para>
/// </remarks>
[StableApi(Since = "0.1.30")]
public sealed record FilterFieldPolicy
{
    /// <summary>
    /// 不做任何限制：服务端自己构造的过滤器用它。任何公共属性路径（引用导航、集合、<c>byte[]</c>、值对象）
    /// 都可以做谓词终点，只有「属性不存在」会被拒绝（那是拼写错误不是准入问题）。
    /// </summary>
    public static FilterFieldPolicy Unrestricted { get; } = new() { AllowNavigation = true, MaxDepth = int.MaxValue, AllowAnyProperty = true };

    /// <summary>
    /// 请求数据的宽松策略：根实体的每一个公共标量属性都可用，禁止导航路径。
    /// 它<b>不</b>保护根列上的秘密（口令哈希、安全戳、薪资）—— 端点知道自己投影什么时用
    /// <see cref="ProjectedBy"/>（<c>CrudAppService</c> 的默认），只有实体上没有敏感根列时才该直接用它。
    /// </summary>
    public static FilterFieldPolicy Request { get; } = new();

    /// <summary>
    /// 是否允许含 <c>.</c> 的导航路径（默认 false）。开启后中间段可以是引用导航，终点仍须是标量。
    /// </summary>
    public bool AllowNavigation { get; init; }

    /// <summary>
    /// 字段路径允许的最大段数（默认 1 = 只有根属性）。只在 <see cref="AllowNavigation"/> 开启时有意义。
    /// </summary>
    public int MaxDepth { get; init; } = 1;

    /// <summary>
    /// 是否放弃标量 / 集合规则，只要求路径上的每一段是公共属性（默认 false）。
    /// <see cref="Unrestricted"/> 开它；请求来源的策略不该开 —— 没有标量终点的约束，
    /// <c>IsNull</c> / <c>IsNotNull</c> 就能对任何导航回答存在性。
    /// </summary>
    public bool AllowAnyProperty { get; init; }

    /// <summary>
    /// 显式允许的字段路径（不区分大小写）。为 null 时按标量 / 导航规则判定；非 null 时只有名单内的路径可用，
    /// 且名单内的路径同样要在实体上存在并以标量结尾。
    /// </summary>
    public IReadOnlySet<string>? AllowedFields { get; init; }

    /// <summary>
    /// 只允许 <paramref name="projectionType"/>（通常是端点返回的 DTO）上公共标量属性同名的根字段：
    /// 请求方只能按它本来就看得见的列筛选与排序。名单里的名字仍要在实体上存在并且是标量，
    /// DTO 上有而实体上没有（计算属性、改过名的字段）的名字不会放行任何东西。
    /// </summary>
    /// <remarks>
    /// <see cref="Request"/> 放行根实体的每一个标量列，而口令哈希、安全戳、令牌、薪资恰恰就是根列：
    /// 只挡导航路径挡不住对它们的二分探测。按投影收窄之后，实体上多出来的列对请求方不存在。
    /// </remarks>
    public static FilterFieldPolicy ProjectedBy(Type projectionType)
    {
        Check.NotNull(projectionType);
        var projected = projectionType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => IsScalar(p.PropertyType))
            .Select(p => p.Name);

        return new FilterFieldPolicy
        {
            AllowedFields = new HashSet<string>(projected, StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// 只允许点名的字段路径（可含导航段，如 <c>"Customer.Name"</c>）
    /// </summary>
    public static FilterFieldPolicy AllowOnly(params string[] fields)
    {
        Check.NotNullOrEmpty(fields);
        return new FilterFieldPolicy
        {
            AllowNavigation = true,
            MaxDepth = int.MaxValue,
            AllowedFields = new HashSet<string>(fields, StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// 判定一条字段路径在 <paramref name="rootType"/> 上是否被本策略允许。
    /// 「不存在」与「存在但不允许」刻意不区分 —— 区分了就是隐藏列的存在性预言机。
    /// </summary>
    public bool IsAllowed(Type rootType, string fieldPath)
    {
        Check.NotNull(rootType);
        if (string.IsNullOrWhiteSpace(fieldPath))
            return false;

        if (AllowedFields != null && !AllowedFields.Contains(fieldPath))
            return false;

        var segments = fieldPath.Split('.');
        if (segments.Length > MaxDepth || (segments.Length > 1 && !AllowNavigation))
            return false;

        var currentType = rootType;
        for (var i = 0; i < segments.Length; i++)
        {
            var property = currentType.GetProperty(segments[i], BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
            if (property == null)
                return false;

            var isLast = i == segments.Length - 1;
            if (isLast)
                return AllowAnyProperty || IsScalar(property.PropertyType);

            // 中间段必须是引用导航（不能是集合，也不能是标量）；AllowAnyProperty 时只要属性存在
            if (!AllowAnyProperty && (IsScalar(property.PropertyType) || IsCollection(property.PropertyType)))
                return false;

            currentType = property.PropertyType;
        }

        return false;
    }

    /// <summary>
    /// 标量 = 能直接落成 SQL 列的类型：基元、string、decimal、Guid、日期时间族、TimeSpan、枚举，及其 Nullable
    /// </summary>
    public static bool IsScalar(Type type)
    {
        Check.NotNull(type);
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive
            || underlying.IsEnum
            || underlying == typeof(string)
            || underlying == typeof(decimal)
            || underlying == typeof(Guid)
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset)
            || underlying == typeof(DateOnly)
            || underlying == typeof(TimeOnly)
            || underlying == typeof(TimeSpan);
    }

    private static bool IsCollection(Type type)
    {
        return type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
    }
}
