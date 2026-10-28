namespace Tnzi.Imaging.Services;

/// <summary>
/// 验证码服务实现
/// </summary>
public class VerifyCodeService : IVerifyCodeService
{
    private readonly ICache? _cache;
    private readonly ValidateCoder _validateCoder;
    private const string CacheKeyPrefix = "VerifyCode:";

    /// <summary>
    /// 「已消费」标记的前缀。刻意不挂在 <see cref="CacheKeyPrefix"/> 下面：id 可以由调用方指定，
    /// 共用前缀时 id 为 <c>consumed:x</c> 的验证码数据键会与 id 为 <c>x</c> 的标记键撞在一起。
    /// </summary>
    private const string ConsumedKeyPrefix = "VerifyCodeConsumed:";

    /// <summary>
    /// 标记的保留时长。它只需活得比「读到数据到抢标记」那段窗口久；同一个 id 重新签发时标记会被清掉，
    /// 所以留长一点也不会挡住复用 id 的调用方。
    /// </summary>
    private static readonly TimeSpan ConsumedMarkerLifetime = TimeSpan.FromMinutes(60);

    /// <summary>
    /// 初始化一个<see cref="VerifyCodeService"/>类型的新实例
    /// </summary>
    public VerifyCodeService(ValidateCoder validateCoder, ICache? cache = null)
    {
        _validateCoder = Check.NotNull(validateCoder);
        _cache = cache;
    }

    /// <summary>
    /// 生成验证码
    /// </summary>
    public async Task<VerifyCodeResult> GenerateAsync(int codeLength = 4, VerifyCodeType codeType = VerifyCodeType.Number, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        return await GenerateAsync(id, codeLength, codeType, 5, cancellationToken);
    }

    /// <summary>
    /// 生成验证码（指定唯一标识）
    /// </summary>
    public async Task<VerifyCodeResult> GenerateAsync(string id, int codeLength = 4, VerifyCodeType codeType = VerifyCodeType.Number, int expireMinutes = 5, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(id);

        // 映射验证码类型
        var validateCodeType = MapCodeType(codeType);

        // 生成验证码文本（委托给 ValidateCoder 统一管理字符池）
        var code = _validateCoder.GetCode(codeLength, validateCodeType);

        // 生成验证码图片
        var imageBytes = _validateCoder.CreateImageBytes(code, validateCodeType);

        // 存储验证码到缓存（用于后续验证）
        if (_cache != null)
        {
            // 先清掉这个 id 上一次验证留下的「已消费」标记，再写新验证码：反过来的话，
            // 两步之间到达的验证请求会读到新码、却撞上旧标记而失败。
            await _cache.RemoveAsync(GetConsumedKey(id), cancellationToken);

            var cacheKey = GetCacheKey(id);
            await _cache.SetAsync(cacheKey, code, TimeSpan.FromMinutes(expireMinutes), cancellationToken);
        }

        return new VerifyCodeResult
        {
            Code = code,
            ImageBytes = imageBytes,
            Id = id
        };
    }

    /// <summary>
    /// 验证验证码
    /// </summary>
    public async Task<bool> VerifyAsync(string id, string code, bool ignoreCase = true, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        if (string.IsNullOrEmpty(code))
            return false;

        if (_cache == null)
            return false;

        var cacheKey = GetCacheKey(id);
        var storedCode = await _cache.GetAsync<string>(cacheKey, cancellationToken);

        if (string.IsNullOrEmpty(storedCode))
            return false; // 验证码不存在或已过期

        // ★ 一次性由「占住消费标记」保证，不是由后面那次删除保证。先读、再删、后比较三步之间没有原子性：
        //   对同一个 id 并发放进 N 个不同的猜测，N 个都能在任何一个删掉之前读到验证码，4 位数字的命中率
        //   从 1/10000 变成 N/10000。TrySetAsync 在 Redis 上是 SET NX、在内存缓存上持锁，只有第一个请求占得住，
        //   其余一律按已用过处理。写不进标记（部分缓存实现把异常吞成 false）同样拒绝：记不下标记就无从保证一次性。
        if (!await _cache.TrySetAsync(GetConsumedKey(id), true, ConsumedMarkerLifetime, cancellationToken))
            return false;

        // 删除验证码（一次性使用，无论验证成功与否都消耗）
        await _cache.RemoveAsync(cacheKey, cancellationToken);

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(storedCode, code, comparison);
    }

    /// <summary>
    /// 删除验证码
    /// </summary>
    public async Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(id))
            return;

        if (_cache == null)
            return;

        var cacheKey = GetCacheKey(id);
        await _cache.RemoveAsync(cacheKey, cancellationToken);
    }

    /// <summary>
    /// 映射验证码类型
    /// </summary>
    private static ValidateCodeType MapCodeType(VerifyCodeType codeType)
    {
        return codeType switch
        {
            VerifyCodeType.Number => ValidateCodeType.Number,
            VerifyCodeType.Letter => ValidateCodeType.NumberAndLetter,
            VerifyCodeType.Mixed => ValidateCodeType.NumberAndLetter,
            _ => ValidateCodeType.Number
        };
    }

    /// <summary>
    /// 获取缓存键
    /// </summary>
    private static string GetCacheKey(string id)
    {
        return $"{CacheKeyPrefix}{id}";
    }

    private static string GetConsumedKey(string id) => $"{ConsumedKeyPrefix}{id}";
}
