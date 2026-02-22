
namespace Tnzi.Extensions;

/// <summary>
/// 随机数<see cref="Random"/>类型的扩展辅助操作类
/// </summary>
public static class RandomExtensions
{
    /// <summary>
    /// 中国手机号前缀列表（常见号段）
    /// </summary>
    private static readonly string[] TelStarts = new[]
    {
        "134", "135", "136", "137", "138", "139",
        "150", "151", "152", "157", "158", "159",
        "130", "131", "132", "155", "156", "133", "153",
        "180", "181", "182", "183", "185", "186",
        "176", "187", "188", "189", "177", "178"
    };

    /// <summary>
    /// 返回随机布尔值
    /// </summary>
    /// <param name="random"></param>
    /// <returns>随机布尔值</returns>
    public static bool NextBoolean(this Random random)
    {
        return random.NextDouble() > 0.5;
    }

    /// <summary>
    /// 返回指定枚举类型的随机枚举值
    /// </summary>
    /// <param name="random"></param>
    /// <returns>指定枚举类型的随机枚举值</returns>
    public static T NextEnum<T>(this Random random) where T : struct
    {
        Type type = typeof(T);
        if (!type.IsEnum)
        {
            throw new InvalidOperationException("Type must be an enum.");
        }
        Array array = Enum.GetValues(type);
        int index = random.Next(array.GetLowerBound(0), array.GetUpperBound(0) + 1);
        return (T)array.GetValue(index)!;
    }

    /// <summary>
    /// 返回随机数填充的指定长度的数组
    /// </summary>
    /// <param name="random"></param>
    /// <param name="length">数组长度</param>
    /// <returns>随机数填充的指定长度的数组</returns>
    public static byte[] NextBytes(this Random random, int length)
    {
        Check.GreaterThanOrEqual(length, 0);
        byte[] data = new byte[length];
        random.NextBytes(data);
        return data;
    }

    /// <summary>
    /// 返回数组中的随机元素
    /// </summary>
    /// <typeparam name="T">元素类型</typeparam>
    /// <param name="random"></param>
    /// <param name="items">元素数组</param>
    /// <returns>元素数组中的某个随机项</returns>
    public static T NextItem<T>(this Random random, T[] items)
    {
        if (items == null || items.Length == 0)
        {
            return default(T)!;
        }
        return items[random.Next(items.Length)];
    }

    /// <summary>
    /// 返回指定时间段内的随机时间值
    /// </summary>
    /// <param name="random"></param>
    /// <param name="minValue">时间范围的最小值</param>
    /// <param name="maxValue">时间范围的最大值</param>
    /// <returns>指定时间段内的随机时间值</returns>
    public static DateTime NextDateTime(this Random random, DateTime minValue, DateTime maxValue)
    {
        long ticks = minValue.Ticks + (long)((maxValue.Ticks - minValue.Ticks) * random.NextDouble());
        return new DateTime(ticks);
    }

    /// <summary>
    /// 返回随机时间值
    /// </summary>
    /// <param name="random"></param>
    /// <returns>随机时间值</returns>
    public static DateTime NextDateTime(this Random random)
    {
        return NextDateTime(random, DateTime.MinValue, DateTime.MaxValue);
    }

    /// <summary>
    /// 获取指定的长度的随机数字字符串
    /// </summary>
    /// <param name="random"></param>
    /// <param name="length">要获取随机数长度</param>
    /// <returns>指定长度的随机数字符串</returns>
    public static string NextNumberString(this Random random, int length)
    {
        Check.GreaterThanOrEqual(length, 0);
        char[] pattern = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };
        var result = new System.Text.StringBuilder(length);
        int n = pattern.Length;
        for (int i = 0; i < length; i++)
        {
            int rnd = random.Next(0, n);
            result.Append(pattern[rnd]);
        }
        return result.ToString();
    }

    /// <summary>
    /// 获取指定的长度的随机字母字符串
    /// </summary>
    /// <param name="random"></param>
    /// <param name="length">要获取随机数长度</param>
    /// <returns>指定长度的随机字母组成字符串</returns>
    public static string NextLetterString(this Random random, int length)
    {
        Check.GreaterThanOrEqual(length, 0);
        char[] pattern = { 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L',
            'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z' };
        var result = new System.Text.StringBuilder(length);
        int n = pattern.Length;
        for (int i = 0; i < length; i++)
        {
            int rnd = random.Next(0, n);
            result.Append(pattern[rnd]);
        }
        return result.ToString();
    }

    /// <summary>
    /// 获取指定的长度的随机字母和数字字符串
    /// </summary>
    /// <param name="random"></param>
    /// <param name="length">要获取随机数长度</param>
    /// <returns>指定长度的随机字母和数字组成字符串</returns>
    public static string NextLetterAndNumberString(this Random random, int length)
    {
        Check.GreaterThanOrEqual(length, 0);
        char[] pattern = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
            'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P',
            'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z' };
        var result = new System.Text.StringBuilder(length);
        int n = pattern.Length;
        for (int i = 0; i < length; i++)
        {
            int rnd = random.Next(0, n);
            result.Append(pattern[rnd]);
        }
        return result.ToString();
    }

    /// <summary>
    /// 获取随机手机号
    /// </summary>
    /// <param name="random"></param>
    /// <param name="sections">指定手机号段, 号段字符串必须是7位数值</param>
    /// <returns></returns>
    public static string NextPhoneNumber(this Random random, params string[] sections)
    {
        int index = random.Next(0, TelStarts.Length - 1);
        string section;
        if (sections.Length == 0)
        {
            string first = TelStarts[index];
            string second = (random.Next(100, 9876) + 10000).ToString()[1..];
            section = first + second;
        }
        else
        {
            section = random.NextItem(sections);
        }

        string third = (random.Next(1, 9100) + 10000).ToString()[1..];
        return section + third;
    }
}