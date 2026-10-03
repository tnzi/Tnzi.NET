
namespace Tnzi.Identity.Services;

/// <summary>
/// 验证码服务实现
/// </summary>
public class CaptchaService : ApplicationService, ICaptchaService
{
    private readonly ICache? _cache;
    private readonly CaptchaOptions _captchaOptions;
    private readonly ValidateCoder _validateCoder;
    private const int DefaultExpirationSeconds = 300; // 5分钟
    private const int DefaultCodeLength = 4;
    private const int FailureRecordExpirationMinutes = 30; // 失败记录保留 30 分钟（固定窗口，自第一次失败起算）

    public CaptchaService(
        IOptionsSnapshot<IdentityOptions> identityOptions,
        IServiceProvider serviceProvider,
        ICache? cache = null)
        : base(serviceProvider)
    {
        // Scoped 服务：IOptionsSnapshot 每请求重算，CaptchaFailThreshold 随请求热更新。
        _captchaOptions = Check.NotNull(identityOptions).Value.Captcha ?? new CaptchaOptions();
        _cache = cache;
        _validateCoder = new ValidateCoder
        {
            FontSize = 24,
            RandomColor = true,
            RandomLineCount = 3,
            RandomPointPercent = 5
        };
    }

    /// <inheritdoc />
    public bool IsCacheAvailable => _cache != null;

    /// <inheritdoc />
    public async Task<CaptchaResult> GenerateAsync(string purpose)
    {
        // 缓存不可用时抛出异常，避免生成无法验证的验证码
        if (_cache == null)
        {
            throw new InvalidOperationException(
                "Captcha service requires cache to be available. " +
                "Please register ICache implementation in DI container.");
        }

        // 验证码ID使用无连字符格式（"N"），以减少长度并提高URL友好性
        var captchaId = Guid.NewGuid().ToString("N");
        var code = _validateCoder.GetCode(DefaultCodeLength, ValidateCodeType.NumberAndLetter);
        var imageBytes = _validateCoder.CreateImageBytes(code, ValidateCodeType.NumberAndLetter);

        // 存储验证码到缓存
        var cacheKey = CacheKeys.Identity.Captcha(purpose, captchaId);
        await _cache.SetAsync(cacheKey, code, TimeSpan.FromSeconds(DefaultExpirationSeconds));

        return new CaptchaResult
        {
            CaptchaId = captchaId,
            ImageBytes = imageBytes,
            ExpirationSeconds = DefaultExpirationSeconds
        };
    }

    /// <inheritdoc />
    public async Task<bool> VerifyAsync(string captchaId, string captchaCode, string purpose)
    {
        if (_cache == null || string.IsNullOrEmpty(captchaId) || string.IsNullOrEmpty(captchaCode))
            return false;

        var cacheKey = CacheKeys.Identity.Captcha(purpose, captchaId);
        var storedCode = await _cache.GetAsync<string>(cacheKey);

        if (string.IsNullOrEmpty(storedCode))
            return false;

        // ★ 一次性由「占住消费标记」保证，不是由后面那次删除保证。先读、再删、后判定三步之间没有原子性：
        //   同一个 id:code 并发放进 N 个请求，N 个都能在任何一个删掉之前读到它，于是一张验证码开了 N 次门。
        //   TrySetAsync 在 Redis 上是 SET NX、在内存缓存上持锁，只有第一个请求占得住；其余一律按已用过处理。
        //   标记的寿命与验证码本身一致，过期后这个 id 本来也查不到了。
        var claimed = await _cache.TrySetAsync(ConsumedKey(cacheKey), true, TimeSpan.FromSeconds(DefaultExpirationSeconds));

        // 验证后删除（一次性使用）
        await _cache.RemoveAsync(cacheKey);

        return claimed && string.Equals(storedCode, captchaCode, StringComparison.OrdinalIgnoreCase);
    }

    private static string ConsumedKey(string captchaCacheKey) => $"{captchaCacheKey}:consumed";

    /// <inheritdoc />
    public async Task RecordLoginFailureAsync(string identifier)
    {
        if (_cache == null || string.IsNullOrEmpty(identifier))
            return;

        // ★ 原子递增，不是读-改-写：并发的失败登录同时读到 k、各写 k+1，最终计数远小于实际次数，
        //   「N 次失败后要验证码」的闸门就能被并发喷洒推迟。
        //   ★ 窗口是固定的不是滑动的：这个 IncrementAsync 重载只在键不存在时设过期，之后的递增不再延长它
        //   （见 ICache.IncrementAsync 的说明）—— 第一次失败起算 30 分钟，之后的失败不把窗口往后推。
        //   此前的 SetAsync 是每次失败都重设 30 分钟的滑动窗口；改成原子递增时窗口语义随之改变，
        //   对「N 次失败后要验证码」这道闸门没有影响（阈值只看计数），但推理它时不要按滑动窗口来。
        var cacheKey = CacheKeys.WithTenant(CacheKeys.Identity.LoginFailure(identifier), CurrentUser?.TenantId);
        await _cache.IncrementAsync(cacheKey, 1, TimeSpan.FromMinutes(FailureRecordExpirationMinutes));
    }

    /// <inheritdoc />
    public async Task<int> GetLoginFailureCountAsync(string identifier)
    {
        if (_cache == null || string.IsNullOrEmpty(identifier))
            return 0;

        // 计数一律经 GetCounterAsync 读：IncrementAsync 存的是 long，GetAsync<int?> 在内存缓存下必然落空并读成 0
        // （见 ICache.GetCounterAsync 的注释），那会让闸门永不触发。
        var cacheKey = CacheKeys.WithTenant(CacheKeys.Identity.LoginFailure(identifier), CurrentUser?.TenantId);
        var count = await _cache.GetCounterAsync(cacheKey);
        return count > int.MaxValue ? int.MaxValue : (int)count;
    }

    /// <inheritdoc />
    public async Task ClearLoginFailureAsync(string identifier)
    {
        if (_cache == null || string.IsNullOrEmpty(identifier))
            return;

        var cacheKey = CacheKeys.WithTenant(CacheKeys.Identity.LoginFailure(identifier), CurrentUser?.TenantId);
        await _cache.RemoveAsync(cacheKey);
    }

    /// <inheritdoc />
    public async Task<bool> IsCaptchaRequiredAsync(string identifier)
    {
        // 如果阈值为0或负数，表示禁用此功能
        if (_captchaOptions.CaptchaFailThreshold <= 0)
            return false;

        var failureCount = await GetLoginFailureCountAsync(identifier);
        return failureCount >= _captchaOptions.CaptchaFailThreshold;
    }


}
