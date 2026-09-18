namespace Tnzi.Redis.Tests.Fakes;

/// <summary>
/// 基于 Moq 的内存 Redis 假实现，用真实字典作后备存储，
/// 使 RedisCacheService 的读写方法可以进行真正的往返（round-trip）验证：
/// 一个方法写入的键，另一个方法能读到——这正是"双存储格式"bug 修复后要保证的不变量。
/// </summary>
/// <remarks>
/// 仅实现 RedisCacheService / RedisDistributedLock 用到的 IDatabase 成员子集，
/// String 与 Set 两类数据结构分别用独立字典承载，String 写入遵守 <see cref="When.NotExists"/> 语义。
/// 另模拟一个单端点的 <see cref="IServer"/>，使按模式 / 前缀扫描（<c>SCAN</c>）的路径也能往返验证。
/// </remarks>
internal sealed class InMemoryRedis
{
    private readonly object _gate = new();
    private static readonly System.Net.EndPoint Endpoint = new System.Net.DnsEndPoint("localhost", 6379);

    /// <summary>String 类型后备存储（键为完整 Redis 键，含实例前缀）。</summary>
    public Dictionary<string, string> Strings { get; } = new();

    /// <summary>Set 类型后备存储（用于标签索引）。</summary>
    public Dictionary<string, HashSet<string>> Sets { get; } = new();

    /// <summary>键的 TTL（毫秒）。只由计数器脚本的 <c>pexpire</c> 写入；键删除时一并清掉。</summary>
    public Dictionary<string, long> Expiries { get; } = new();

    /// <summary>每个键被设置过几次 TTL —— 固定窗口语义要求同一窗口内只有一次。</summary>
    public Dictionary<string, int> ExpirySetCounts { get; } = new();

    public Mock<IConnectionMultiplexer> Multiplexer { get; }
    public Mock<IDatabase> Database { get; }
    public Mock<IServer> Server { get; }

    public InMemoryRedis()
    {
        Database = new Mock<IDatabase>(MockBehavior.Loose);
        var batch = BuildBatch();

        // ---- String 写 ----
        Database
            .Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .Returns((RedisKey k, RedisValue v, TimeSpan? _, When when) => Task.FromResult(SetString(k, v, when)));
        Database
            .Setup(d => d.StringSet(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .Returns((RedisKey k, RedisValue v, TimeSpan? _, When when) => SetString(k, v, when));

        // ---- String 读 ----
        Database
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey k, CommandFlags _) => Task.FromResult(GetString(k)));
        Database
            .Setup(d => d.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey k, CommandFlags _) => GetString(k));
        Database
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey[] keys, CommandFlags _) => Task.FromResult(keys.Select(GetString).ToArray()));

        // ---- 计数器 ----
        Database
            .Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey k, long by, CommandFlags _) => Task.FromResult(Increment(k, by)));
        Database
            .Setup(d => d.StringDecrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey k, long by, CommandFlags _) => Task.FromResult(Increment(k, -by)));

        // ---- 计数器脚本：INCRBY + 仅当无 TTL 时 PEXPIRE（RedisCacheService.IncrementAsync 的固定窗口实现）----
        Database
            .Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .Returns((string script, RedisKey[] keys, RedisValue[] values, CommandFlags _) =>
                Task.FromResult(EvaluateScript(script, keys, values)));

        // ---- Key 生命周期 ----
        Database
            .Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(true));
        Database
            .Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey k, CommandFlags _) => Task.FromResult(ContainsKey(k)));
        Database
            .Setup(d => d.KeyExists(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey k, CommandFlags _) => ContainsKey(k));
        Database
            .Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey k, CommandFlags _) => Task.FromResult(DeleteKey(k)));
        Database
            .Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey[] keys, CommandFlags _) => Task.FromResult((long)keys.Count(DeleteKey)));

        // ---- Set（标签索引）----
        Database
            .Setup(d => d.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey k, RedisValue v, CommandFlags _) => Task.FromResult(SetAdd(k, v)));
        Database
            .Setup(d => d.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey k, CommandFlags _) => Task.FromResult(SetMembers(k)));

        // ---- 批量 ----
        Database
            .Setup(d => d.CreateBatch(It.IsAny<object>()))
            .Returns(batch.Object);

        // ---- 键空间扫描（RemoveByPattern / RemoveByPrefix / Clear）----
        Server = new Mock<IServer>(MockBehavior.Loose);
        Server.SetupGet(s => s.IsConnected).Returns(true);
        Server.SetupGet(s => s.IsReplica).Returns(false);
        Server
            .Setup(s => s.Keys(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns((int _, RedisValue pattern, int _, long _, int _, CommandFlags _) => MatchingKeys(pattern));
        Server
            .Setup(s => s.KeysAsync(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns((int _, RedisValue pattern, int _, long _, int _, CommandFlags _) => AsAsync(MatchingKeys(pattern)));

        Multiplexer = new Mock<IConnectionMultiplexer>(MockBehavior.Loose);
        Multiplexer
            .Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(Database.Object);
        Multiplexer.Setup(m => m.GetEndPoints(It.IsAny<bool>())).Returns([Endpoint]);
        Multiplexer.Setup(m => m.GetServer(It.IsAny<System.Net.EndPoint>(), It.IsAny<object>())).Returns(Server.Object);
    }

    private static async IAsyncEnumerable<RedisKey> AsAsync(IEnumerable<RedisKey> keys)
    {
        foreach (var key in keys)
        {
            await Task.Yield();
            yield return key;
        }
    }

    /// <summary>按 Redis glob（只支持 <c>*</c>）匹配当前全部键，快照后返回。</summary>
    private RedisKey[] MatchingKeys(RedisValue pattern)
    {
        var glob = pattern.IsNullOrEmpty ? "*" : pattern.ToString();
        var regex = new System.Text.RegularExpressions.Regex(
            "^" + System.Text.RegularExpressions.Regex.Escape(glob).Replace("\\*", ".*") + "$");
        lock (_gate)
        {
            return Strings.Keys.Concat(Sets.Keys).Where(k => regex.IsMatch(k)).Select(k => (RedisKey)k).ToArray();
        }
    }

    private Mock<IBatch> BuildBatch()
    {
        var batch = new Mock<IBatch>(MockBehavior.Loose);
        batch
            .Setup(b => b.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .Returns((RedisKey k, RedisValue v, TimeSpan? _, When when) => Task.FromResult(SetString(k, v, when)));
        batch.Setup(b => b.Execute());
        return batch;
    }

    private bool SetString(RedisKey key, RedisValue value, When when)
    {
        lock (_gate)
        {
            var k = key.ToString();
            if (when == When.NotExists && Strings.ContainsKey(k))
                return false;
            Strings[k] = value.ToString();
            return true;
        }
    }

    private RedisValue GetString(RedisKey key)
    {
        lock (_gate)
        {
            return Strings.TryGetValue(key.ToString(), out var s) ? (RedisValue)s : RedisValue.Null;
        }
    }

    private long Increment(RedisKey key, long by)
    {
        lock (_gate)
        {
            var k = key.ToString();
            long current = Strings.TryGetValue(k, out var s) && long.TryParse(s, out var parsed) ? parsed : 0;
            current += by;
            Strings[k] = current.ToString();
            return current;
        }
    }

    private RedisResult EvaluateScript(string script, RedisKey[] keys, RedisValue[] values)
    {
        if (!script.Contains("incrby", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"InMemoryRedis does not emulate this script: {script}");
        }

        lock (_gate)
        {
            var k = keys[0].ToString();
            var value = Increment(keys[0], (long)values[0]);
            if (script.Contains("pttl", StringComparison.OrdinalIgnoreCase) && !Expiries.ContainsKey(k))
            {
                Expiries[k] = (long)values[1];
                ExpirySetCounts[k] = ExpirySetCounts.GetValueOrDefault(k) + 1;
            }

            return RedisResult.Create((RedisValue)value, ResultType.Integer);
        }
    }

    private bool ContainsKey(RedisKey key)
    {
        lock (_gate)
        {
            var k = key.ToString();
            return Strings.ContainsKey(k) || Sets.ContainsKey(k);
        }
    }

    private bool DeleteKey(RedisKey key)
    {
        lock (_gate)
        {
            var k = key.ToString();
            var removed = Strings.Remove(k);
            removed |= Sets.Remove(k);
            Expiries.Remove(k);
            return removed;
        }
    }

    private bool SetAdd(RedisKey key, RedisValue value)
    {
        lock (_gate)
        {
            var k = key.ToString();
            if (!Sets.TryGetValue(k, out var set))
            {
                set = new HashSet<string>();
                Sets[k] = set;
            }
            return set.Add(value.ToString());
        }
    }

    private RedisValue[] SetMembers(RedisKey key)
    {
        lock (_gate)
        {
            return Sets.TryGetValue(key.ToString(), out var set)
                ? set.Select(x => (RedisValue)x).ToArray()
                : Array.Empty<RedisValue>();
        }
    }
}
