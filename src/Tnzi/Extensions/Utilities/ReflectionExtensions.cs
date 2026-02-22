
namespace Tnzi.Extensions;

/// <summary>
/// 反射扩展方法
/// </summary>
public static class ReflectionExtensions
{
    #region 特性操作

    /// <summary>
    /// 获取成员上的指定特性
    /// </summary>
    public static T? GetAttribute<T>(this MemberInfo member, bool inherit = true) where T : Attribute
    {
        return member.GetCustomAttribute<T>(inherit);
    }

    /// <summary>
    /// 检查成员是否有指定特性
    /// </summary>
    public static bool HasAttribute<T>(this MemberInfo member, bool inherit = true) where T : Attribute
    {
        return member.IsDefined(typeof(T), inherit);
    }

    /// <summary>
    /// 获取类型上的Description特性描述信息
    /// </summary>
    public static string GetDescription(this Type type, bool inherit = true)
    {
        var desc = type.GetAttribute<DescriptionAttribute>(inherit);
        return desc?.Description ?? type.FullName ?? type.Name;
    }

    /// <summary>
    /// 获取成员上的Description特性描述信息
    /// </summary>
    public static string GetDescription(this MemberInfo member, bool inherit = true)
    {
        var display = member.GetAttribute<DisplayAttribute>(inherit);
        if (display?.Name != null)
            return display.Name;

        var displayName = member.GetAttribute<DisplayNameAttribute>(inherit);
        if (displayName?.DisplayName != null)
            return displayName.DisplayName;

        var desc = member.GetAttribute<DescriptionAttribute>(inherit);
        if (desc?.Description != null)
            return desc.Description;

        return member.Name;
    }

    #endregion

    #region 属性操作

    /// <summary>
    /// 获取对象的属性值
    /// </summary>
    public static object? GetPropertyValue(this object obj, string propertyName)
    {
        if (obj == null || string.IsNullOrEmpty(propertyName))
            return null;

        var property = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(obj);
    }

    /// <summary>
    /// 设置对象的属性值
    /// </summary>
    public static void SetPropertyValue(this object obj, string propertyName, object? value)
    {
        if (obj == null || string.IsNullOrEmpty(propertyName))
            return;

        var property = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        property?.SetValue(obj, value);
    }

    #endregion

    #region 类型判断

    /// <summary>
    /// 判断类型是否为可空类型
    /// </summary>
    public static bool IsNullableType(this Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
    }

    /// <summary>
    /// 获取可空类型的基础类型
    /// </summary>
    public static Type GetUnderlyingType(this Type type)
    {
        return Nullable.GetUnderlyingType(type) ?? type;
    }

    /// <summary>
    /// 判断类型是否为数值类型
    /// </summary>
    public static bool IsNumericType(this Type type)
    {
        var underlyingType = type.GetUnderlyingType();
        return underlyingType == typeof(int) ||
               underlyingType == typeof(long) ||
               underlyingType == typeof(short) ||
               underlyingType == typeof(byte) ||
               underlyingType == typeof(uint) ||
               underlyingType == typeof(ulong) ||
               underlyingType == typeof(ushort) ||
               underlyingType == typeof(sbyte) ||
               underlyingType == typeof(float) ||
               underlyingType == typeof(double) ||
               underlyingType == typeof(decimal);
    }

    /// <summary>
    /// 判断类型是否派生自指定类型
    /// </summary>
    public static bool IsDeriveClassFrom(this Type type, Type baseType, bool canAbstract = false)
    {
        if (type == null || baseType == null)
            return false;

        return type.IsClass && (canAbstract || !type.IsAbstract) && baseType.IsAssignableFrom(type);
    }

    /// <summary>
    /// 判断类型是否派生自指定类型
    /// </summary>
    public static bool IsDeriveClassFrom<TBaseType>(this Type type, bool canAbstract = false)
    {
        return IsDeriveClassFrom(type, typeof(TBaseType), canAbstract);
    }

    #endregion
}