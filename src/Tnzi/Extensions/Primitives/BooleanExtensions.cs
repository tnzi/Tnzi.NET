
namespace Tnzi.Extensions;

/// <summary>
/// 布尔值<see cref="Boolean"/>类型的扩展辅助操作类
/// </summary>
public static class BooleanExtensions
{
    /// <summary>
    /// 把布尔值转换为小写字符串
    /// </summary>
    public static string ToLower(this bool value)
    {
        return value.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// 如果条件成立, 则抛出异常
    /// </summary>
    public static void TrueThrow(this bool flag, Exception exception)
    {
        Check.NotNull(exception);
        if (flag)
        {
            throw exception;
        }
    }
}