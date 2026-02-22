
namespace Tnzi.Extensions;

/// <summary>
/// 枚举扩展方法
/// </summary>
public static class EnumExtensions
{
    /// <summary>
    /// 获取枚举项上的<see cref="DescriptionAttribute"/>特性的文字描述
    /// </summary>
    /// <param name="value">枚举值</param>
    /// <param name="inherit">是否搜索成员的继承链以查找这些特性</param>
    /// <returns>枚举项的描述文字</returns>
    public static string ToDescription(this Enum value, bool inherit = false)
    {
        if (value == null)
            return string.Empty;

        Type type = value.GetType();
        string? name = Enum.GetName(type, value);
        if (name == null)
            return string.Empty;

        FieldInfo? field = type.GetField(name);
        if (field == null)
            return name;

        DescriptionAttribute? desc = field.GetCustomAttribute<DescriptionAttribute>(inherit);
        return desc?.Description ?? name;
    }
}