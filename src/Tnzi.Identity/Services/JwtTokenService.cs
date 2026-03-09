
namespace Tnzi.Identity.Services;

/// <summary>
/// JWT令牌服务实现
/// </summary>
public class JwtTokenService : ApplicationService, ITokenService
{
    private readonly JwtOptions _jwtOptions;
    private readonly string _secretKey;
    private readonly bool _multiTenancyEnabled;

    /// <summary>
    /// 初始化一个<see cref="JwtTokenService"/>类型的新实例
    /// </summary>
    public JwtTokenService(
        IOptions<IdentityOptions> identityOptions,
        IOptions<MultiTenancyOptions>? multiTenancyOptions,
        IServiceProvider serviceProvider,
        IWebHostEnvironment? environment = null)
        : base(serviceProvider)
    {
        _jwtOptions = Check.NotNull(identityOptions).Value.Jwt;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;

        // 安全检查：生产环境必须配置 JWT 密钥
        _secretKey = _jwtOptions.SecretKey;
        if (string.IsNullOrEmpty(_secretKey))
        {
            var isProduction = environment?.EnvironmentName == "Production";
            if (isProduction)
            {
                throw new InvalidOperationException(
                    "JWT SecretKey must be configured in production environment. " +
                    "Please set 'Identity:Jwt:SecretKey' in your configuration.");
            }
            // 仅在非生产环境使用默认密钥，并记录警告
            _secretKey = "Tnzi_Default_Secret_Key_For_Dev_Only_123456";
            LogWarning("Using default JWT secret key. This is NOT secure for production use!");
        }
    }

    /// <summary>
    /// 生成JWT令牌
    /// </summary>
    public string GenerateToken(User user, IList<string> roles)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName!),
            // JWT JTI 使用默认格式（包含连字符），符合 JWT 标准规范
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        if (_multiTenancyEnabled && user.TenantId.HasValue)
        {
            claims.Add(new Claim("tenant_id", user.TenantId.Value.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes);

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// 生成刷新令牌
    /// 使用URL安全的Base64编码，确保可以在URL中安全传递
    /// </summary>
    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        // 使用URL安全的Base64编码，避免URL不安全的字符（+、/、=）
        return WebEncoders.Base64UrlEncode(randomNumber);
    }

    /// <summary>
    /// 从过期的令牌获取Principal
    /// </summary>
    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey)),
            ValidateLifetime = false // 不验证过期时间
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken securityToken);

        if (securityToken is not JwtSecurityToken jwtSecurityToken ||
            !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityTokenException("Invalid token");
        }

        return principal;
    }

    /// <summary>
    /// 生成令牌结果
    /// </summary>
    public TokenResult GenerateTokenResult(User user, IList<string> roles)
    {
        var accessToken = GenerateToken(user, roles);
        var refreshToken = GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes);

        return new TokenResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            ExpiresIn = _jwtOptions.AccessTokenExpirationMinutes * 60,
            RefreshTokenExpiresIn = _jwtOptions.EnableRefreshToken
                ? _jwtOptions.RefreshTokenExpirationDays * 24 * 60 * 60
                : null
        };
    }
}
