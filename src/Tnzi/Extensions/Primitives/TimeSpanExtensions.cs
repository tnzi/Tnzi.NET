
namespace Tnzi.Extensions;

/// <summary>
/// 时间片<see cref="TimeSpan"/>扩展方法
/// </summary>
public static class TimeSpanExtensions
{
    /// <summary>
    /// 将时间片转换为X时X分X秒的格式
    /// </summary>
    /// <param name="ts"></param>
    /// <returns></returns>
    public static string ToTimeString(this TimeSpan ts)
    {
        var parts = new List<string>();
        
        if (ts.Days > 0)
            parts.Add($"{ts.Days} days");
        if (ts.Hours > 0)
            parts.Add($"{ts.Hours} hours");
        if (ts.Minutes > 0)
            parts.Add($"{ts.Minutes} mins");
        if (ts.Seconds > 0)
            parts.Add($"{ts.Seconds:D2} secs");

        return parts.Count > 0 ? string.Join(" ", parts) : "0 secs";
    }

    /// <summary>
    /// 时间片倒计时（同步版本，会阻塞线程）
    /// 警告：此方法会阻塞当前线程，建议使用异步版本 CountDownAsync
    /// </summary>
    public static void CountDown(this TimeSpan ts, Action<TimeSpan> action, int intervalMilliseconds = 1000, Func<bool>? breakFunc = null)
    {
        Check.NotNull(action);

        while (ts > TimeSpan.Zero)
        {
            if (breakFunc != null && breakFunc())
            {
                break;
            }
            action(ts);
            var ts2 = TimeSpan.FromMilliseconds(intervalMilliseconds);
            Thread.Sleep(ts2); // 同步阻塞，在同步方法中这是必要的
            ts = ts.Subtract(ts2);
        }
    }

    /// <summary>
    /// 时间片倒计时（异步）
    /// </summary>
    public static async Task CountDownAsync(this TimeSpan ts, Func<TimeSpan, Task> action, int intervalMilliseconds = 1000, Func<Task<bool>>? breakFunc = null)
    {
        Check.NotNull(action);

        while (ts > TimeSpan.Zero)
        {
            if (breakFunc != null && await breakFunc())
            {
                break;
            }
            await action(ts);
            TimeSpan ts2 = TimeSpan.FromMilliseconds(intervalMilliseconds);
            await Task.Delay(ts2);
            ts = ts.Subtract(ts2);
        }
    }
}