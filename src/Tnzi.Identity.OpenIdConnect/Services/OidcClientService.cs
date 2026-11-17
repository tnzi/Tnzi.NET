using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using OpenIddict.Core;

namespace Tnzi.Identity.OpenIdConnect.Services;

/// <summary>
/// <see cref="IOidcClientService"/> 的实现：写入一律经 OpenIddict 的应用管理器（它负责密钥哈希、
/// 回调地址与权限的协议级校验），本类负责把框架的 DTO 翻成 OpenIddict 的描述符，并维护启停语义。
/// </summary>
public class OidcClientService : ApplicationService, IOidcClientService
{
    /// <summary>客户端密钥的随机字节数（256 位）。</summary>
    public const int SecretBytes = 32;

    private readonly OpenIddictApplicationManager<OidcClient> _applications;
    private readonly OpenIddictAuthorizationManager<OidcAuthorization> _authorizations;
    private readonly OpenIddictTokenManager<OidcToken> _tokens;
    private readonly IRepository<OidcClient, Guid> _repository;
    private readonly IOptionsMonitor<OpenIdConnectOptions> _options;

    /// <summary>创建服务。</summary>
    public OidcClientService(
        IServiceProvider serviceProvider,
        OpenIddictApplicationManager<OidcClient> applications,
        OpenIddictAuthorizationManager<OidcAuthorization> authorizations,
        OpenIddictTokenManager<OidcToken> tokens,
        IRepository<OidcClient, Guid> repository,
        IOptionsMonitor<OpenIdConnectOptions> options)
        : base(serviceProvider)
    {
        _applications = Check.NotNull(applications);
        _authorizations = Check.NotNull(authorizations);
        _tokens = Check.NotNull(tokens);
        _repository = Check.NotNull(repository);
        _options = Check.NotNull(options);
    }

    /// <inheritdoc />
    public virtual async Task<Result<OidcClientSecretDto>> CreateAsync(CreateOidcClientDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var clientId = string.IsNullOrWhiteSpace(input.ClientId) ? GenerateClientId() : input.ClientId.Trim();
        if (await _applications.FindByClientIdAsync(clientId, cancellationToken) != null)
        {
            return Fail<OidcClientSecretDto>($"A client with client_id '{clientId}' already exists.", 409, ErrorCodes.IDENTITY_OIDC_CLIENT_ALREADY_EXISTS);
        }

        var shape = ValidateShape(input.RedirectUris, input.PostLogoutRedirectUris, input.Scopes);
        if (!shape.Succeeded)
        {
            return Fail<OidcClientSecretDto>(shape.Message!, 400, shape.ErrorCode ?? ErrorCodes.VALIDATION_ERROR);
        }

        var client = new OidcClient
        {
            IsEnabled = true,
            Description = NullIfBlank(input.Description),
            ExternalReference = NullIfBlank(input.ExternalReference),
        };

        var descriptor = BuildDescriptor(clientId, input.DisplayName.Trim(), input.ClientType, input.ConsentType, input.RequirePkce, shape.Data!);
        var secret = input.ClientType == OidcClientType.Confidential ? GenerateSecret() : null;

        var written = await WriteAsync(async () =>
        {
            await _applications.PopulateAsync(client, descriptor, cancellationToken);
            await _applications.CreateAsync(client, secret, cancellationToken);
        });
        if (!written.Succeeded)
        {
            return Fail<OidcClientSecretDto>(written.Message!, 400, written.ErrorCode);
        }

        return Ok(new OidcClientSecretDto { Client = ToDto(client), ClientSecret = secret });
    }

    /// <inheritdoc />
    public virtual async Task<Result<OidcClientDto>> UpdateAsync(Guid id, UpdateOidcClientDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var client = await _applications.FindByIdAsync(id.ToString(), cancellationToken);
        if (client == null)
        {
            return Fail<OidcClientDto>("Client not found.", 404, ErrorCodes.IDENTITY_OIDC_CLIENT_NOT_FOUND);
        }

        var shape = ValidateShape(input.RedirectUris, input.PostLogoutRedirectUris, input.Scopes);
        if (!shape.Succeeded)
        {
            return Fail<OidcClientDto>(shape.Message!, 400, shape.ErrorCode ?? ErrorCodes.VALIDATION_ERROR);
        }

        var clientType = ReadClientType(client);
        var descriptor = BuildDescriptor(client.ClientId!, input.DisplayName.Trim(), clientType, input.ConsentType, input.RequirePkce, shape.Data!);
        client.Description = NullIfBlank(input.Description);
        client.ExternalReference = NullIfBlank(input.ExternalReference);

        var written = await WriteAsync(async () =>
        {
            // 描述符不带密钥：PopulateAsync 会把 ClientSecret 写成描述符里的值（null），所以先记下再放回去。
            var secretHash = client.ClientSecret;
            await _applications.PopulateAsync(client, descriptor, cancellationToken);
            client.ClientSecret = secretHash;
            await _applications.UpdateAsync(client, cancellationToken);
        });
        if (!written.Succeeded)
        {
            return Fail<OidcClientDto>(written.Message!, 400, written.ErrorCode);
        }

        return Ok(ToDto(client));
    }

    /// <inheritdoc />
    public virtual async Task<Result<OidcClientDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var client = await _repository.AsQueryable().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return client == null ? Fail<OidcClientDto>("Client not found.", 404, ErrorCodes.IDENTITY_OIDC_CLIENT_NOT_FOUND) : Ok(ToDto(client));
    }

    /// <inheritdoc />
    public virtual async Task<Result<IPagedList<OidcClientDto>>> GetPagedListAsync(OidcClientQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var keyword = query.Keyword?.Trim().ToLower();
        var filtered = _repository.AsQueryable().AsNoTracking()
            .Where(c =>
                (string.IsNullOrEmpty(keyword)
                    || c.ClientId!.ToLower().Contains(keyword)
                    || (c.DisplayName != null && c.DisplayName.ToLower().Contains(keyword))) &&
                (string.IsNullOrEmpty(query.ExternalReference) || c.ExternalReference == query.ExternalReference) &&
                (!query.IsEnabled.HasValue || c.IsEnabled == query.IsEnabled.Value));

        var total = await filtered.CountAsync(cancellationToken);
        var items = await filtered
            .OrderByDescending(c => c.CreationTime)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken);

        return Ok<IPagedList<OidcClientDto>>(new PagedList<OidcClientDto>(items.Select(ToDto).ToList(), query.PageIndex, query.PageSize, total));
    }

    /// <inheritdoc />
    public virtual async Task<OidcClientDto?> FindByClientIdAsync(string clientId, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(clientId);
        var client = await _repository.AsQueryable().AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);
        return client == null ? null : ToDto(client);
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<OidcClientDto>> FindByExternalReferenceAsync(string externalReference, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(externalReference);
        var clients = await _repository.AsQueryable().AsNoTracking()
            .Where(c => c.ExternalReference == externalReference)
            .OrderBy(c => c.CreationTime)
            .ToListAsync(cancellationToken);
        return clients.Select(ToDto).ToList();
    }

    /// <inheritdoc />
    public virtual async Task<Result> SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default)
    {
        var client = await _applications.FindByIdAsync(id.ToString(), cancellationToken);
        if (client == null)
        {
            return Fail("Client not found.", 404, ErrorCodes.IDENTITY_OIDC_CLIENT_NOT_FOUND);
        }

        if (client.IsEnabled != enabled)
        {
            client.IsEnabled = enabled;
            await _applications.UpdateAsync(client, cancellationToken);
        }

        if (!enabled)
        {
            // 停用 = 现有授权与令牌全部作废。再次启用不复活它们：用户要重新走一次登录。
            await _authorizations.RevokeByApplicationIdAsync(id.ToString(), cancellationToken);
            await _tokens.RevokeByApplicationIdAsync(id.ToString(), cancellationToken);
        }

        return Ok();
    }

    /// <inheritdoc />
    public virtual async Task<Result<OidcClientSecretDto>> RotateSecretAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var client = await _applications.FindByIdAsync(id.ToString(), cancellationToken);
        if (client == null)
        {
            return Fail<OidcClientSecretDto>("Client not found.", 404, ErrorCodes.IDENTITY_OIDC_CLIENT_NOT_FOUND);
        }

        if (ReadClientType(client) != OidcClientType.Confidential)
        {
            return Fail<OidcClientSecretDto>("Public clients have no secret.", 400, ErrorCodes.IDENTITY_OIDC_CLIENT_HAS_NO_SECRET);
        }

        var secret = GenerateSecret();
        await _applications.UpdateAsync(client, secret, cancellationToken);
        return Ok(new OidcClientSecretDto { Client = ToDto(client), ClientSecret = secret });
    }

    /// <inheritdoc />
    public virtual async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var client = await _applications.FindByIdAsync(id.ToString(), cancellationToken);
        if (client == null)
        {
            return Fail("Client not found.", 404, ErrorCodes.IDENTITY_OIDC_CLIENT_NOT_FOUND);
        }

        // OpenIddict 的删除连同它的授权与令牌一起删。
        await _applications.DeleteAsync(client, cancellationToken);
        return Ok();
    }

    /// <summary>把实体翻成 DTO（协议字段在 OpenIddict 实体里以 JSON 数组存放）。</summary>
    public static OidcClientDto ToDto(OidcClient client)
    {
        Check.NotNull(client);

        var permissions = ReadArray(client.Permissions);
        return new OidcClientDto
        {
            Id = client.Id,
            ClientId = client.ClientId ?? string.Empty,
            DisplayName = client.DisplayName,
            Description = client.Description,
            ClientType = ReadClientType(client),
            ConsentType = string.Equals(client.ConsentType, ConsentTypes.Implicit, StringComparison.Ordinal) ? OidcConsentType.Implicit : OidcConsentType.Explicit,
            IsEnabled = client.IsEnabled,
            RequirePkce = ReadArray(client.Requirements).Contains(Requirements.Features.ProofKeyForCodeExchange),
            RedirectUris = ReadArray(client.RedirectUris),
            PostLogoutRedirectUris = ReadArray(client.PostLogoutRedirectUris),
            Scopes = permissions
                .Where(p => p.StartsWith(ClientPermissions.Prefixes.Scope, StringComparison.Ordinal))
                .Select(p => p[ClientPermissions.Prefixes.Scope.Length..])
                .ToList(),
            ExternalReference = client.ExternalReference,
            CreationTime = client.CreationTime,
        };
    }

    /// <summary>
    /// 校验回调地址与作用域。回调地址必须是绝对 URL、不带片段；要求 HTTPS 时只放行 https 与本机回环的 http。
    /// </summary>
    protected virtual Result<ValidatedShape> ValidateShape(List<string>? redirectUris, List<string>? postLogoutRedirectUris, List<string>? scopes)
    {
        if (redirectUris is not { Count: > 0 })
        {
            return Result<ValidatedShape>.Failure("At least one redirect URI is required.", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var requireHttps = _options.CurrentValue.RequireHttps;
        var redirects = new List<Uri>();
        foreach (var value in redirectUris.Concat(postLogoutRedirectUris ?? []))
        {
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Fragment))
            {
                return Result<ValidatedShape>.Failure($"'{value}' is not an absolute URI without a fragment.", 400, ErrorCodes.VALIDATION_ERROR);
            }

            var secure = uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || !requireHttps));
            if (!secure)
            {
                return Result<ValidatedShape>.Failure($"'{value}' must use https (http is accepted only for loopback addresses).", 400, ErrorCodes.VALIDATION_ERROR);
            }

            redirects.Add(uri);
        }

        var allowed = AllowedScopes();
        var requested = (scopes is { Count: > 0 } ? scopes : [Scopes.Profile, Scopes.Email])
            .Select(s => s.Trim())
            .Where(s => s.Length > 0 && s != Scopes.OpenId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var unknown = requested.Where(s => !allowed.Contains(s)).ToList();
        if (unknown.Count > 0)
        {
            return Result<ValidatedShape>.Failure($"Unknown scope(s): {string.Join(", ", unknown)}.", 400, ErrorCodes.VALIDATION_ERROR);
        }

        return Result<ValidatedShape>.Success(new ValidatedShape(
            redirects.Take(redirectUris.Count).ToList(),
            redirects.Skip(redirectUris.Count).ToList(),
            requested));
    }

    /// <summary>客户端可以被允许的全部作用域（标准作用域减去 openid，加上配置的附加作用域）。</summary>
    protected HashSet<string> AllowedScopes()
        => OidcConstants.StandardScopes.Where(s => s != Scopes.OpenId)
            .Concat(_options.CurrentValue.AdditionalScopes ?? [])
            .ToHashSet(StringComparer.Ordinal);

    private static OpenIddictApplicationDescriptor BuildDescriptor(
        string clientId, string displayName, OidcClientType clientType, OidcConsentType consentType, bool requirePkce, ValidatedShape shape)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            DisplayName = displayName,
            ClientType = clientType == OidcClientType.Public ? ClientTypes.Public : ClientTypes.Confidential,
            ConsentType = consentType == OidcConsentType.Implicit ? ConsentTypes.Implicit : ConsentTypes.Explicit,
        };

        descriptor.RedirectUris.UnionWith(shape.RedirectUris);
        descriptor.PostLogoutRedirectUris.UnionWith(shape.PostLogoutRedirectUris);
        descriptor.Permissions.UnionWith(
        [
            ClientPermissions.Endpoints.Authorization,
            ClientPermissions.Endpoints.Token,
            ClientPermissions.Endpoints.EndSession,
            ClientPermissions.Endpoints.Revocation,
            ClientPermissions.GrantTypes.AuthorizationCode,
            ClientPermissions.ResponseTypes.Code,
        ]);

        if (shape.Scopes.Contains(Scopes.OfflineAccess))
        {
            descriptor.Permissions.Add(ClientPermissions.GrantTypes.RefreshToken);
        }

        descriptor.Permissions.UnionWith(shape.Scopes.Select(s => ClientPermissions.Prefixes.Scope + s));

        // ★ 公开客户端没有密钥，授权码被截获就等于令牌被截获：PKCE 对它不可关闭。
        if (clientType == OidcClientType.Public || requirePkce)
        {
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        }

        return descriptor;
    }

    private static async Task<Result> WriteAsync(Func<Task> write)
    {
        try
        {
            await write();
            return Result.Success();
        }
        catch (OpenIddictExceptions.ValidationException ex)
        {
            return Result.Failure(string.Join(" ", ex.Results.Select(r => r.ErrorMessage)), 400, ErrorCodes.VALIDATION_ERROR);
        }
    }

    private static OidcClientType ReadClientType(OidcClient client)
        => string.Equals(client.ClientType, ClientTypes.Public, StringComparison.Ordinal) ? OidcClientType.Public : OidcClientType.Confidential;

    private static List<string> ReadArray(string? json)
        => string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string GenerateClientId() => "oidc-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));

    private static string GenerateSecret() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));

    /// <summary>校验通过的回调地址与作用域。</summary>
    /// <param name="RedirectUris">登录回调地址。</param>
    /// <param name="PostLogoutRedirectUris">登出后回跳地址。</param>
    /// <param name="Scopes">作用域（不含 openid）。</param>
    protected sealed record ValidatedShape(List<Uri> RedirectUris, List<Uri> PostLogoutRedirectUris, List<string> Scopes);
}
