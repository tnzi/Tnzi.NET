
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
    /// 框架自管的保留 claim 类型；extraClaims 中的这些类型会被忽略，以防通过自定义 claim 伪造身份/角色/租户。
    /// </summary>
    private static readonly HashSet<string> ReservedClaimTypes = new(StringComparer.Ordinal)
    {
        // 长 URI 形式（调用方可能直接传 ClaimTypes.*）
        ClaimTypes.NameIdentifier,
        ClaimTypes.Name,
        ClaimTypes.Role,
        // JWT 短名形式 —— 攻击向量是入站：调用方经 extraClaims 直接传 JWT 短名
        // ("role"/"nameid"/"sub" 等)，验证端 MapInboundClaims=true 会把它们映射回
        // 保留语义的长 URI；故短名必须一并拒绝，否则可绕过过滤伪造身份/提权。
        "nameid",
        "unique_name",
        "name",
        "role",
        "roles",
        JwtRegisteredClaimNames.Sub,  // "sub"
        JwtRegisteredClaimNames.Jti,  // "jti"
        "tenant_id",
    };

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
    public string GenerateToken(User user, IList<string> roles, IEnumerable<Claim>? extraClaims = null)
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

        // 追加调用方自定义 claim，跳过框架自管的保留类型，
        // 避免通过 extraClaims 伪造身份/角色/租户。
        if (extraClaims is not null)
        {
            foreach (var claim in extraClaims)
            {
                if (IsReservedClaimType(claim.Type))
                {
                    continue;
                }

                claims.Add(claim);
            }
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
    /// 判断 claim 是否落到框架保留语义（身份/角色/租户/标准 JWT 声明）。
    /// 同时检查长 URI、JWT 短名，以及 WriteToken outbound 映射后的有效名 ——
    /// 防止调用方经 extraClaims 用短名（如 "role"/"nameid"）绕过过滤伪造身份/提权。
    /// </summary>
    private static bool IsReservedClaimType(string? claimType)
    {
        if (string.IsNullOrEmpty(claimType))
        {
            return true; // 畸形 claim（空 Type）一律拒绝
        }

        if (ReservedClaimTypes.Contains(claimType))
        {
            return true;
        }

        // 兜底（攻击的真实方向是入站）：凡 inbound 映射结果落在保留集合内的短名
        // 一律拒绝，覆盖未来 .NET 新增的短名→长 URI 映射，无需手工维护短名清单。
        return JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.TryGetValue(claimType, out var mapped)
            && ReservedClaimTypes.Contains(mapped);
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
    public TokenResult GenerateTokenResult(User user, IList<string> roles, IEnumerable<Claim>? extraClaims = null)
    {
        var accessToken = GenerateToken(user, roles, extraClaims);
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
