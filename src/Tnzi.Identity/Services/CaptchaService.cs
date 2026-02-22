
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
    private const int FailureRecordExpirationMinutes = 30; // 失败记录保留30分钟

    public CaptchaService(
        IOptions<IdentityOptions> identityOptions,
        IServiceProvider serviceProvider,
        ICache? cache = null)
        : base(serviceProvider)
    {
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

        // 验证后删除（一次性使用）
        await _cache.RemoveAsync(cacheKey);

        return string.Equals(storedCode, captchaCode, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task RecordLoginFailureAsync(string identifier)
    {
        if (_cache == null || string.IsNullOrEmpty(identifier))
            return;

        var cacheKey = CacheKeys.Identity.LoginFailure(identifier);
        var currentCount = await _cache.GetAsync<int?>(cacheKey) ?? 0;
        await _cache.SetAsync(cacheKey, currentCount + 1, TimeSpan.FromMinutes(FailureRecordExpirationMinutes));
    }

    /// <inheritdoc />
    public async Task<int> GetLoginFailureCountAsync(string identifier)
    {
        if (_cache == null || string.IsNullOrEmpty(identifier))
            return 0;

        var cacheKey = CacheKeys.Identity.LoginFailure(identifier);
        return await _cache.GetAsync<int?>(cacheKey) ?? 0;
    }

    /// <inheritdoc />
    public async Task ClearLoginFailureAsync(string identifier)
    {
        if (_cache == null || string.IsNullOrEmpty(identifier))
            return;

        var cacheKey = CacheKeys.Identity.LoginFailure(identifier);
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
