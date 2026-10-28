
namespace Tnzi.Utilities;

/// <summary>
/// 有序Guid生成器 (Sequential GUID)
/// 用于数据库主键，提高索引性能
/// </summary>
/// <remarks>
/// <para>
/// 「有序」只对某一种比较规则成立，而各数据库比较 Guid 列的规则不同，所以按数据库选排列
/// （<see cref="SequentialGuidType"/>）。三种排列都由 48 位 Unix 毫秒时间戳 + 12 位计数器 + 随机位组成，
/// 区别只在于把时间戳与计数器放在<b>目标库最先比较的那几个字节</b>上：
/// </para>
/// <list type="bullet">
/// <item><see cref="SequentialGuidType.SequentialAsString"/>：按 <see cref="Guid.ToString()"/> 的十六进制顺序（= RFC 9562 大端字节序）
/// 有序，是标准的 UUIDv7。存成文本的 MySQL <c>char(36)</c> / SQLite <c>TEXT</c> 与 PostgreSQL <c>uuid</c> 都按这个顺序比较。</item>
/// <item><see cref="SequentialGuidType.SequentialAtEnd"/>：按 SQL Server <c>uniqueidentifier</c> 的比较顺序有序
/// （先比 <see cref="Guid.ToByteArray()"/> 的第 10-15 字节，再比 8-9、6-7、4-5、0-3）。</item>
/// <item><see cref="SequentialGuidType.SequentialAsBinary"/>：按 <see cref="Guid.ToByteArray()"/> 逐字节有序，
/// 给以 .NET 字节序存成 16 字节原始值的列（如 Oracle <c>RAW(16)</c>）。</item>
/// </list>
/// <para>
/// 同一进程内连续生成的 Id 在各自的比较规则下严格递增（时钟回拨除外）；跨进程靠 60 位以上的随机位保证唯一。
/// 同一毫秒内超过 4096 个时等到下一毫秒。
/// </para>
/// </remarks>
public static class SequentialGuid
{
    private static readonly object _syncRoot = new();
    private static long _lastTimestamp = 0;
    private static int _counter = 0;

    /// <summary>
    /// 每毫秒计数器的最大值（12 位）。三种排列共用一个计数器，取最窄的那个字段（UUIDv7 的 rand_a）的位宽。
    /// </summary>
    private const int MaxCounter = 0xFFF;

    /// <summary>
    /// 随机字节数：三种排列里用随机字节最多的那一种所需的量。
    /// </summary>
    private const int RandomByteCount = 8;

    /// <summary>
    /// 有序Guid类型
    /// </summary>
    public enum SequentialGuidType
    {
        /// <summary>
        /// 按 <see cref="Guid.ToString()"/> 的文本顺序有序（标准 UUIDv7）：MySql、Sqlite、PostgreSQL 中适用
        /// </summary>
        SequentialAsString,

        /// <summary>
        /// 按 <see cref="Guid.ToByteArray()"/> 的字节顺序有序：以 .NET 字节序存成 16 字节原始值的列（如 Oracle RAW(16)）中适用
        /// </summary>
        SequentialAsBinary,

        /// <summary>
        /// 按 SQL Server uniqueidentifier 的比较顺序（最后一段最先比较）有序：SqlServer 中适用
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
        // 密码学安全的随机字节（RandomNumberGenerator.Fill 是线程安全的静态方法）
        var random = new byte[RandomByteCount];
        RandomNumberGenerator.Fill(random);

        (long timestamp, int counter) = GetOrderedComponents();

        return Compose(guidType, timestamp, counter, random);
    }

    /// <summary>
    /// 按排列把时间戳、计数器与随机字节拼成 Guid。与时钟无关，各排列在边界上的有序性由测试直接驱动它验证。
    /// </summary>
    /// <param name="guidType">排列</param>
    /// <param name="timestamp">Unix 毫秒时间戳（取低 48 位）</param>
    /// <param name="counter">同一毫秒内的计数器（0 到 <see cref="MaxCounter"/>）</param>
    /// <param name="random">至少 <see cref="RandomByteCount"/> 个随机字节</param>
    private static Guid Compose(SequentialGuidType guidType, long timestamp, int counter, byte[] random)
    {
        Span<byte> bytes = stackalloc byte[16];

        switch (guidType)
        {
            case SequentialGuidType.SequentialAsString:
                // 按大端字节序（= ToString 的十六进制顺序 = PostgreSQL uuid 的比较顺序）构造 UUIDv7：
                // [0-5] 时间戳 | [6] 版本 7 + 计数器高 4 位 | [7] 计数器低 8 位 | [8] 变体 10 + 6 位随机 | [9-15] 随机
                // 不能按 ToByteArray 的下标去填：Guid 的前三段（Data1/2/3）在 ToByteArray 里是小端，ToString 会把它们倒过来
                WriteTimestamp(bytes[..6], timestamp);
                bytes[6] = (byte)(0x70 | ((counter >> 8) & 0x0F));
                bytes[7] = (byte)counter;
                bytes[8] = (byte)(0x80 | (random[0] & 0x3F));
                random.AsSpan(1, 7).CopyTo(bytes[9..]);
                return new Guid(bytes, bigEndian: true);

            case SequentialGuidType.SequentialAsBinary:
                // 按 ToByteArray 逐字节比较：
                // [0-5] 时间戳 | [6] 计数器高 8 位 | [7] 版本 8 + 计数器低 4 位 | [8] 变体 10 + 6 位随机 | [9-15] 随机
                // 版本号是 Data3 的高 4 位，而 Data3 在 ToByteArray 里小端存放，所以版本号落在第 7 字节的高半字节
                WriteTimestamp(bytes[..6], timestamp);
                bytes[6] = (byte)(counter >> 4);
                bytes[7] = (byte)(0x80 | (counter & 0x0F));
                bytes[8] = (byte)(0x80 | (random[0] & 0x3F));
                random.AsSpan(1, 7).CopyTo(bytes[9..]);
                return new Guid(bytes);

            case SequentialGuidType.SequentialAtEnd:
            default:
                // SQL Server 先比较 ToByteArray 的第 10-15 字节，再比较 8-9，最后才是 6-7、4-5、0-3：
                // [0-6] 随机 | [7] 版本 8 + 4 位随机 | [8] 变体 10 + 计数器高 4 位 | [9] 计数器低 8 位 | [10-15] 时间戳
                // 时间戳必须完整的 6 字节：只放低 32 位时每 2^32 毫秒（约 49.7 天）回绕一次，插入点跳回索引中部
                random.AsSpan(0, 7).CopyTo(bytes);
                bytes[7] = (byte)(0x80 | (random[7] & 0x0F));
                bytes[8] = (byte)(0x80 | ((counter >> 8) & 0x0F));
                bytes[9] = (byte)counter;
                WriteTimestamp(bytes[10..], timestamp);
                return new Guid(bytes);
        }
    }

    /// <summary>
    /// 把时间戳的低 48 位按大端序写进 6 个字节
    /// </summary>
    private static void WriteTimestamp(Span<byte> destination, long timestamp)
    {
        for (var i = 0; i < 6; i++)
        {
            destination[i] = (byte)(timestamp >> (40 - 8 * i));
        }
    }

    /// <summary>
    /// 获取有序组件（Unix 毫秒时间戳和计数器）
    /// 线程安全：所有对共享状态的访问都在 lock 内
    /// </summary>
    private static (long timestamp, int counter) GetOrderedComponents()
    {
        lock (_syncRoot)
        {
            long timestamp = CurrentUnixMilliseconds();

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
                if (_counter > MaxCounter)
                {
                    // 计数器溢出，等待下一毫秒（同一毫秒内超过 4096 个 Guid 时发生）
                    // 使用 Thread.Yield() 让出 CPU 时间片，避免 CPU 空转
                    while (timestamp <= _lastTimestamp)
                    {
                        Thread.Yield(); // 让出当前线程的时间片，允许其他线程执行
                        timestamp = CurrentUnixMilliseconds();
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

            return (_lastTimestamp, _counter);
        }
    }

    private static long CurrentUnixMilliseconds()
        => (DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond;

    /// <summary>
    /// 从 SequentialAtEnd 类型的 GUID 中提取时间戳（UTC 时间）
    /// 注意：此方法仅适用于 SequentialAtEnd 类型的 GUID（SQL Server 排序，也是 <see cref="NewGuid()"/> 的默认排列）
    /// </summary>
    /// <param name="id">SequentialAtEnd 类型的 GUID</param>
    /// <returns>UTC 时间（DateTime），精确到毫秒；如果其中的时间戳超出 DateTime 的范围则返回 DateTime.MinValue</returns>
    public static DateTime GetDateFrom(Guid id)
    {
        Span<byte> guidBytes = stackalloc byte[16];
        id.TryWriteBytes(guidBytes);

        // SequentialAtEnd 布局：时间戳是第 [10-15] 字节上的 48 位大端 Unix 毫秒
        long timestamp = 0;
        for (var i = 10; i < 16; i++)
        {
            timestamp = (timestamp << 8) | guidBytes[i];
        }

        var maxMilliseconds = (DateTime.MaxValue.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond;
        return timestamp > maxMilliseconds
            ? DateTime.MinValue
            : new DateTime(DateTime.UnixEpoch.Ticks + timestamp * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }
}
