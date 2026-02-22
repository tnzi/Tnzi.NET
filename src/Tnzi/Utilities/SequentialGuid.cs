
namespace Tnzi.Utilities;

/// <summary>
/// 有序Guid生成器 (Sequential GUID)
/// 用于数据库主键，提高索引性能
/// </summary>
public static class SequentialGuid
{
    private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();
    private static readonly object _syncRoot = new();
    private static long _lastTimestamp = 0;
    private static int _counter = 0;

    /// <summary>
    /// 有序Guid类型
    /// </summary>
    public enum SequentialGuidType
    {
        /// <summary>
        /// 将GUID当作字符串来处理的前排序, MySql, Sqlite, PostgreSQL中适用
        /// </summary>
        SequentialAsString,

        /// <summary>
        /// 将GUID作为二进制处理的排序, Oracle, PostgreSQL中适用
        /// </summary>
        SequentialAsBinary,

        /// <summary>
        /// 按GUID的最后一段来排序, SqlServer中适用
        /// </summary>
        SequentialAtEnd
    }

    /// <summary>
    /// 生成有序Guid (默认用于SQL Server)
    /// </summary>
    public static Guid NewGuid() => NewGuid(SequentialGuidType.SequentialAtEnd);

    /// <summary>
    /// 生成有序Guid
    /// </summary>
    /// <param name="guidType">Guid类型</param>
    /// <returns>有序Guid</returns>
    public static Guid NewGuid(SequentialGuidType guidType)
    {
        // 1. 生成密码学安全的随机字节（RandomNumberGenerator 是线程安全的）
        byte[] randomBytes = new byte[10];
        _rng.GetBytes(randomBytes);

        // 2. 获取有序组件（时间戳 + 计数器）
        (long timestamp, ushort counter) = GetOrderedComponents();
        byte[] timestampBytes = GetTimestampBytes(timestamp);

        // 3. 构造GUID
        byte[] guidBytes = new byte[16];

        switch (guidType)
        {
            case SequentialGuidType.SequentialAsString:
                // 字符串排序布局：随机(6)+时间戳(6)+计数器(2)+随机(2)
                // 字节布局: [0-5:随机] [6-11:时间戳] [12-13:计数器] [14-15:随机]
                Buffer.BlockCopy(randomBytes, 0, guidBytes, 0, 6);
                Buffer.BlockCopy(timestampBytes, 0, guidBytes, 6, 6);
                guidBytes[12] = (byte)(counter >> 8);
                guidBytes[13] = (byte)counter;
                Buffer.BlockCopy(randomBytes, 6, guidBytes, 14, 2);
                // 注意：字节6是时间戳的一部分，字节8也是时间戳的一部分，不能设置版本位和变体位
                break;

            case SequentialGuidType.SequentialAsBinary:
                // 二进制排序布局：时间戳(6)+计数器(2)+随机(8)
                // 字节布局: [0-5:时间戳] [6-7:计数器] [8-15:随机]
                Buffer.BlockCopy(timestampBytes, 0, guidBytes, 0, 6);
                guidBytes[6] = (byte)(counter >> 8);
                guidBytes[7] = (byte)counter;
                Buffer.BlockCopy(randomBytes, 0, guidBytes, 8, 8);
                // 注意：字节6是计数器的高字节，不能设置版本位；字节8是随机字节，可以设置变体位
                // 设置变体位 (字节8的高2位: 10xxxxxx)
                guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
                break;

            case SequentialGuidType.SequentialAtEnd:
            default:
                // SQL Server末尾排序布局：随机(10)+时间戳(4)+计数器(2)
                // 字节布局: [0-9:随机] [10-13:时间戳] [14-15:计数器]
                Buffer.BlockCopy(randomBytes, 0, guidBytes, 0, 10);
                Buffer.BlockCopy(timestampBytes, 2, guidBytes, 10, 4);
                guidBytes[14] = (byte)(counter >> 8);
                guidBytes[15] = (byte)counter;
                // 注意：字节6和8都在随机部分，可以安全设置版本位和变体位
                // 设置版本位 (字节6的高4位: 0100xxxx)
                guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x40);
                // 设置变体位 (字节8的高2位: 10xxxxxx)
                guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
                break;
        }

        return new Guid(guidBytes);
    }

    /// <summary>
    /// 获取有序组件（时间戳和计数器）
    /// 线程安全：所有对共享状态的访问都在 lock 内
    /// </summary>
    private static (long timestamp, ushort counter) GetOrderedComponents()
    {
        lock (_syncRoot)
        {
            long timestamp = DateTime.UtcNow.Ticks / 10000L; // 毫秒时间戳

            if (timestamp > _lastTimestamp)
            {
                // 时间戳递增，重置计数器
                _lastTimestamp = timestamp;
                _counter = 0;
            }
            else if (timestamp == _lastTimestamp)
            {
                // 同一毫秒内，递增计数器
                _counter++;
                if (_counter > 65535) // ushort最大值 (0xFFFF)
                {
                    // 计数器溢出，等待下一毫秒（这种情况极少发生：需要在同一毫秒内生成超过65535个GUID）
                    // 使用 Thread.Yield() 让出 CPU 时间片，避免 CPU 空转
                    while (timestamp <= _lastTimestamp)
                    {
                        Thread.Yield(); // 让出当前线程的时间片，允许其他线程执行
                        timestamp = DateTime.UtcNow.Ticks / 10000L;
                    }
                    _lastTimestamp = timestamp;
                    _counter = 0;
                }
            }
            else
            {
                // 时钟回拨（系统时间被调整向后），使用当前时间戳并重置计数器
                // 这可能导致生成的 GUID 不严格递增，但保证了唯一性
                _lastTimestamp = timestamp;
                _counter = 0;
            }

            return (_lastTimestamp, (ushort)_counter);
        }
    }

    /// <summary>
    /// 将时间戳转换为字节数组（6字节，大端序）
    /// 时间戳是毫秒级，从 DateTime.UtcNow.Ticks / 10000 计算得出
    /// 使用6字节可以表示约 2^48 毫秒 ≈ 8925 年，足够实际使用
    /// </summary>
    private static byte[] GetTimestampBytes(long timestamp)
    {
        byte[] bytes = BitConverter.GetBytes(timestamp);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }
        // 取后6字节（低48位），确保大端序
        // 对于实际使用场景（从1970-01-01开始），前2字节通常为0或很小的值
        byte[] result = new byte[6];
        Buffer.BlockCopy(bytes, 2, result, 0, 6);
        return result;
    }

    /// <summary>
    /// 从 SequentialAtEnd 类型的 GUID 中提取时间戳（UTC 时间）
    /// 注意：此方法仅适用于 SequentialAtEnd 类型的 GUID（SQL Server 排序）
    /// </summary>
    /// <param name="id">SequentialAtEnd 类型的 GUID</param>
    /// <returns>UTC 时间（DateTime），如果 GUID 格式不正确则返回 DateTime.MinValue</returns>
    public static DateTime GetDateFrom(Guid id)
    {
        byte[] guidBytes = id.ToByteArray();

        // SequentialAtEnd 布局：随机(10)+时间戳(4)+计数器(2)
        // 时间戳在 [10-13] 字节，是大端序的4字节（从6字节时间戳的后4字节）
        // 需要重建完整的6字节时间戳（前2字节通常为0）
        byte[] timestampBytes = new byte[6];
        timestampBytes[0] = 0;
        timestampBytes[1] = 0;
        Buffer.BlockCopy(guidBytes, 10, timestampBytes, 2, 4);

        // timestampBytes 现在是大端序的6字节时间戳
        // 需要转换为小端序以便 BitConverter 使用
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(timestampBytes);
        }

        // 将6字节时间戳转换为 long（时间戳是毫秒级）
        // BitConverter.ToInt64 需要8字节，我们在开头补2个0字节
        byte[] fullBytes = new byte[8];
        Buffer.BlockCopy(timestampBytes, 0, fullBytes, 2, 6);
        long timestamp = BitConverter.ToInt64(fullBytes, 0);

        // 将毫秒时间戳转换为 DateTime（UTC）
        // 时间戳是从 DateTime.UtcNow.Ticks / 10000 计算得出的
        long ticks = timestamp * 10000L;
        return new DateTime(ticks, DateTimeKind.Utc);
    }
}