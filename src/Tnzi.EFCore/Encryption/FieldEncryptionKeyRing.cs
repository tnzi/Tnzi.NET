namespace Tnzi.EFCore.Encryption;

/// <summary>
/// 字段加密的主密钥环:把配置里的 <see cref="FieldEncryptionOptions.Keys"/> 与密钥文件合并成一份解码后的快照,
/// 并在配置或文件变化时重建。
/// </summary>
/// <remarks>
/// <para>
/// 快照在选项对象换了一份、或密钥文件的修改时间 / 长度变了时重建。文件状态最多每 <see cref="FileCheckIntervalMs"/>
/// 毫秒检查一次(每行数据的加解密都经过这里,不能每次都访问文件系统)。
/// </para>
/// <para>
/// 重建失败(例如替换密钥文件时读到写了一半的内容)时沿用上一份可用快照并记 Warning,不让一次瞬时读失败打断全部读写;
/// 从未成功加载过则抛出 <see cref="FieldEncryptionException"/>。
/// </para>
/// </remarks>
public sealed class FieldEncryptionKeyRing
{
    /// <summary>密钥文件状态的最短检查间隔(毫秒)。</summary>
    public const int FileCheckIntervalMs = 2000;

    private readonly IOptionsMonitor<FieldEncryptionOptions> _options;
    private readonly ILogger<FieldEncryptionKeyRing>? _logger;
    private readonly object _gate = new();
    private Snapshot? _snapshot;

    /// <summary>
    /// 初始化 <see cref="FieldEncryptionKeyRing"/>。
    /// </summary>
    public FieldEncryptionKeyRing(IOptionsMonitor<FieldEncryptionOptions> options, ILogger<FieldEncryptionKeyRing>? logger = null)
    {
        _options = Check.NotNull(options);
        _logger = logger;
    }

    /// <summary>当前活动密钥标识,没有则为 <c>null</c>。</summary>
    public string? ActiveKeyId => GetSnapshot().ActiveKeyId;

    /// <summary>密钥标识是否在密钥环里。</summary>
    public bool Contains(string keyId) => GetSnapshot().Keys.ContainsKey(keyId);

    /// <summary>取指定标识的密钥(32 字节,调用方不得修改)。</summary>
    internal bool TryGetKey(string keyId, out byte[] key) => GetSnapshot().Keys.TryGetValue(keyId, out key!);

    /// <summary>取活动密钥。</summary>
    internal bool TryGetActive(out string keyId, out byte[] key)
    {
        var snapshot = GetSnapshot();
        keyId = snapshot.ActiveKeyId ?? string.Empty;
        key = [];
        return snapshot.ActiveKeyId is not null && snapshot.Keys.TryGetValue(snapshot.ActiveKeyId, out key!);
    }

    private Snapshot GetSnapshot()
    {
        var current = _options.CurrentValue;
        var snapshot = _snapshot;
        var now = Environment.TickCount64;

        if (snapshot is not null
            && ReferenceEquals(snapshot.Source, current)
            && (snapshot.FileStamp is null || now - snapshot.CheckedAt < FileCheckIntervalMs))
        {
            return snapshot;
        }

        lock (_gate)
        {
            snapshot = _snapshot;
            if (snapshot is not null && ReferenceEquals(snapshot.Source, current))
            {
                if (snapshot.FileStamp is null || now - snapshot.CheckedAt < FileCheckIntervalMs)
                {
                    return snapshot;
                }

                if (ReadStamp(current.KeyFile) == snapshot.FileStamp)
                {
                    _snapshot = snapshot with { CheckedAt = now };
                    return _snapshot;
                }
            }

            try
            {
                _snapshot = Build(current, now);
            }
            catch (FieldEncryptionException ex) when (snapshot is not null)
            {
                _logger?.LogWarning(ex, "Reloading the field-encryption key ring failed; keeping the previous key ring until the next successful reload.");
                _snapshot = snapshot with { Source = current, CheckedAt = now };
            }

            return _snapshot!;
        }
    }

    private static Snapshot Build(FieldEncryptionOptions options, long now)
    {
        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (keyId, material) in options.Keys)
        {
            keys[keyId] = DecodeKey(keyId, material);
        }

        string? fileActive = null;
        string? stamp = null;
        if (!string.IsNullOrWhiteSpace(options.KeyFile))
        {
            stamp = ReadStamp(options.KeyFile);
            if (!FieldEncryptionKeyFile.TryLoad(options.KeyFile, out var file, out var errors))
            {
                throw new FieldEncryptionException(
                    $"The field-encryption key file could not be loaded: {string.Join(" ", errors)}",
                    isKeyMissing: true);
            }

            fileActive = file.ActiveKeyId;
            foreach (var (keyId, material) in file.Keys)
            {
                if (keys.ContainsKey(keyId))
                {
                    throw new FieldEncryptionException($"Key id '{keyId}' is defined both in configuration and in the key file.");
                }

                keys[keyId] = DecodeKey(keyId, material);
            }
        }

        var active = string.IsNullOrWhiteSpace(options.ActiveKeyId) ? fileActive : options.ActiveKeyId;
        return new Snapshot(options, active, keys, stamp, now);
    }

    private static byte[] DecodeKey(string keyId, string material)
    {
        try
        {
            return Convert.FromBase64String(material);
        }
        catch (FormatException ex)
        {
            throw new FieldEncryptionException($"Key '{keyId}' is not valid Base64.", isKeyMissing: false, ex);
        }
    }

    private static string? ReadStamp(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var info = new FileInfo(path);
        return info.Exists ? $"{info.LastWriteTimeUtc.Ticks}:{info.Length}" : "missing";
    }

    private sealed record Snapshot(
        FieldEncryptionOptions Source,
        string? ActiveKeyId,
        Dictionary<string, byte[]> Keys,
        string? FileStamp,
        long CheckedAt);
}
