
namespace Tnzi.Identity.Services;

/// <summary>
/// 密码策略服务实现
/// </summary>
public class PasswordPolicyService : ApplicationService, IPasswordPolicyService
{
    private readonly IRepository<PasswordHistory, Guid> _repository;
    private readonly UserManager<User> _userManager;
    private readonly IOptionsMonitor<IdentityOptions>? _identityOptionsMonitor;

    private PasswordPolicyOptions PasswordPolicyOptions =>
        _identityOptionsMonitor?.CurrentValue.PasswordPolicy ?? new PasswordPolicyOptions();

    public PasswordPolicyService(
        IRepository<PasswordHistory, Guid> repository,
        UserManager<User> userManager,
        IServiceProvider serviceProvider,
        IOptionsMonitor<IdentityOptions>? identityOptions = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _userManager = Check.NotNull(userManager);
        _identityOptionsMonitor = identityOptions;
    }

    public string? ValidatePasswordStrength(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return "Password cannot be empty";
        }

        var options = PasswordPolicyOptions;

        // 检查最小长度
        if (password.Length < options.MinLength)
        {
            return $"Password must be at least {options.MinLength} characters long";
        }

        // 检查大写字母
        if (options.RequireUppercase && !password.Any(char.IsUpper))
        {
            return "Password must contain at least one uppercase letter";
        }

        // 检查小写字母
        if (options.RequireLowercase && !password.Any(char.IsLower))
        {
            return "Password must contain at least one lowercase letter";
        }

        // 检查数字
        if (options.RequireDigit && !password.Any(char.IsDigit))
        {
            return "Password must contain at least one digit";
        }

        // 检查特殊字符
        if (options.RequireNonAlphanumeric)
        {
            var hasSpecialChar = password.Any(ch => !char.IsLetterOrDigit(ch));
            if (!hasSpecialChar)
            {
                return "Password must contain at least one special character";
            }
        }

        return null; // 验证通过
    }

    public async Task<bool> CheckPasswordHistoryAsync(Guid userId, string newPassword)
    {
        var options = PasswordPolicyOptions;

        // 如果未启用密码历史检查，直接返回false
        if (options.PasswordHistoryCount <= 0)
        {
            return false;
        }

        // 获取用户
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return false;
        }

        // 获取最近的密码历史记录
        var recentHistory = await _repository
            .Where(ph => ph.UserId == userId)
            .OrderByDescending(ph => ph.CreationTime)
            .Take(options.PasswordHistoryCount)
            .ToListAsync();

        // 检查新密码是否与历史密码匹配
        foreach (var history in recentHistory)
        {
            // 使用UserManager验证密码哈希
            var result = _userManager.PasswordHasher.VerifyHashedPassword(user, history.PasswordHash, newPassword);
            if (result == PasswordVerificationResult.Success || result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                Logger.LogWarning("Password found in history for user {UserId}", userId);
                return true;
            }
        }

        return false;
    }

    /// <remarks>
    /// ★★★ <strong>密码历史关着时也写，只留最近一行。</strong>这张表同时是「最近一次改密」的唯一来源，
    /// 密码到期（<see cref="CheckPasswordExpirationAsync"/>）以它为参照。此前历史关着（出厂默认）就一行都不写，
    /// 到期的参照时间永远回退到账号创建时间：开了到期、没开历史的部署里，账号满 N 天之后每次登录都被要求改密，
    /// 改完下一次照样到期，永远走不出去。那一行只是时间戳，复用检查（<see cref="CheckPasswordHistoryAsync"/>）
    /// 在历史关着时照旧不看它；顺带让日后再打开到期时，参照的是真实的最近改密时间。
    /// </remarks>
    public async Task SavePasswordHistoryAsync(Guid userId, string passwordHash)
    {
        var options = PasswordPolicyOptions;
        var retained = Math.Max(options.PasswordHistoryCount, 1);

        // 保存新密码历史
        var passwordHistory = new PasswordHistory
        {
            UserId = userId,
            PasswordHash = passwordHash,
            CreationTime = DateTime.UtcNow
        };

        await _repository.InsertAsync(passwordHistory);

        // 清理超出历史记录数量的旧密码
        // 使用 Take 限制查询数量，减少竞态条件的影响范围
        // 即使并发插入导致记录数超过限制，也只删除超出配置数量的记录
        var allHistory = await _repository
            .Where(ph => ph.UserId == userId)
            .OrderByDescending(ph => ph.CreationTime)
            .Take(retained + 10) // 多查询一些，确保覆盖并发情况
            .ToListAsync();

        if (allHistory.Count > retained)
        {
            // 只保留最新的 retained 条记录
            var toDelete = allHistory.Skip(retained).ToList();
            await _repository.DeleteManyAsync(toDelete);
        }
    }

    public async Task<PasswordExpirationResult> CheckPasswordExpirationAsync(Guid userId)
    {
        var options = PasswordPolicyOptions;

        // 如果未启用密码过期，直接返回
        if (options.PasswordExpirationDays <= 0)
        {
            return PasswordExpirationResult.NotRequired();
        }

        var lastChangeTime = await GetLastPasswordChangeTimeAsync(userId);

        // 如果从未修改过密码，检查用户创建时间
        if (!lastChangeTime.HasValue)
        {
            var user = await _userManager.FindByGuidAsync(userId);
            if (user == null)
            {
                return PasswordExpirationResult.NotRequired();
            }

            // 使用用户创建时间作为密码设置时间
            lastChangeTime = user.CreationTime;
        }

        var expirationDate = lastChangeTime.Value.AddDays(options.PasswordExpirationDays);
        var now = DateTime.UtcNow;

        if (now > expirationDate)
        {
            Logger.LogWarning("Password expired for user {UserId}, expired at {ExpiredAt}", userId, expirationDate);
            return PasswordExpirationResult.Expired(expirationDate);
        }

        var daysUntilExpiration = (int)(expirationDate - now).TotalDays;
        return PasswordExpirationResult.NotExpired(daysUntilExpiration);
    }

    public async Task<DateTime?> GetLastPasswordChangeTimeAsync(Guid userId)
    {
        // 从密码历史中获取最近一次密码修改时间
        var latestHistory = await _repository
            .Where(ph => ph.UserId == userId)
            .OrderByDescending(ph => ph.CreationTime)
            .FirstOrDefaultAsync();

        return latestHistory?.CreationTime;
    }

    public PasswordStrengthResult EvaluatePasswordStrength(string password)
    {
        var result = new PasswordStrengthResult();

        if (string.IsNullOrEmpty(password))
        {
            result.Score = 0;
            result.Level = PasswordStrengthLevel.VeryWeak;
            result.MeetsPolicy = false;
            result.Suggestions.Add("Password cannot be empty");
            return result;
        }

        var score = 0;
        var options = PasswordPolicyOptions;

        // 长度评分（最高30分）
        var lengthScore = Math.Min(password.Length * 3, 30);
        score += lengthScore;

        // 字符类型评分（每类最高10分）
        var hasUppercase = password.Any(char.IsUpper);
        var hasLowercase = password.Any(char.IsLower);
        var hasDigit = password.Any(char.IsDigit);
        var hasSpecialChar = password.Any(ch => !char.IsLetterOrDigit(ch));

        if (hasUppercase) score += 10;
        if (hasLowercase) score += 10;
        if (hasDigit) score += 10;
        if (hasSpecialChar) score += 10;

        // 复杂度奖励（最高20分）
        var charTypeCount = (hasUppercase ? 1 : 0) + (hasLowercase ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSpecialChar ? 1 : 0);
        if (charTypeCount >= 3) score += 10;
        if (charTypeCount >= 4) score += 5;
        if (password.Length >= 12) score += 5;

        // 扣分项：重复字符
        var distinctRatio = (double)password.Distinct().Count() / password.Length;
        if (distinctRatio < 0.5) score -= 10;

        result.Score = Math.Clamp(score, 0, 100);

        // 映射等级
        result.Level = result.Score switch
        {
            < 20 => PasswordStrengthLevel.VeryWeak,
            < 40 => PasswordStrengthLevel.Weak,
            < 60 => PasswordStrengthLevel.Fair,
            < 80 => PasswordStrengthLevel.Strong,
            _ => PasswordStrengthLevel.VeryStrong
        };

        // 生成改进建议
        if (password.Length < options.MinLength)
            result.Suggestions.Add($"Use at least {options.MinLength} characters");
        else if (password.Length < 12)
            result.Suggestions.Add("Consider using 12 or more characters for better security");

        if (!hasUppercase)
            result.Suggestions.Add("Add uppercase letters (A-Z)");
        if (!hasLowercase)
            result.Suggestions.Add("Add lowercase letters (a-z)");
        if (!hasDigit)
            result.Suggestions.Add("Add numbers (0-9)");
        if (!hasSpecialChar)
            result.Suggestions.Add("Add special characters (!@#$%^&*)");

        if (distinctRatio < 0.5)
            result.Suggestions.Add("Avoid repeating characters");

        // 策略合规检查（复用现有验证逻辑）
        result.MeetsPolicy = ValidatePasswordStrength(password) == null;

        return result;
    }

}
