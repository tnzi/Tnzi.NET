
namespace Tnzi.Utilities;

/// <summary>
/// 数据计算辅助操作类
/// </summary>
public static class MathHelper
{
    /// <summary>
    /// 获取两个坐标的距离
    /// </summary>
    /// <param name="x1">第一个点的X坐标</param>
    /// <param name="y1">第一个点的Y坐标</param>
    /// <param name="x2">第二个点的X坐标</param>
    /// <param name="y2">第二个点的Y坐标</param>
    /// <returns>两点之间的距离</returns>
    public static double GetDistance(double x1, double y1, double x2, double y2)
    {
        return Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2));
    }

    /// <summary>
    /// 获取两个坐标的距离（3D）
    /// </summary>
    /// <param name="x1">第一个点的X坐标</param>
    /// <param name="y1">第一个点的Y坐标</param>
    /// <param name="z1">第一个点的Z坐标</param>
    /// <param name="x2">第二个点的X坐标</param>
    /// <param name="y2">第二个点的Y坐标</param>
    /// <param name="z2">第二个点的Z坐标</param>
    /// <returns>两点之间的距离</returns>
    public static double GetDistance3D(double x1, double y1, double z1, double x2, double y2, double z2)
    {
        return Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2) + Math.Pow(z1 - z2, 2));
    }

    /// <summary>
    /// 将值限制在指定范围内
    /// </summary>
    /// <typeparam name="T">数值类型</typeparam>
    /// <param name="value">要限制的值</param>
    /// <param name="min">最小值</param>
    /// <param name="max">最大值</param>
    /// <returns>限制后的值</returns>
    public static T Clamp<T>(T value, T min, T max) where T : IComparable<T>
    {
        if (value.CompareTo(min) < 0)
            return min;
        if (value.CompareTo(max) > 0)
            return max;
        return value;
    }

    /// <summary>
    /// 将角度转换为弧度
    /// </summary>
    public static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    /// <summary>
    /// 将弧度转换为角度
    /// </summary>
    public static double ToDegrees(double radians)
    {
        return radians * 180.0 / Math.PI;
    }
}