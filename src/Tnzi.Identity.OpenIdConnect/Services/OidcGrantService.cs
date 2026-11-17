using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Core;

namespace Tnzi.Identity.OpenIdConnect.Services;

/// <summary>
/// <see cref="IOidcGrantService"/> 的实现。撤销一律同时作废授权与它名下的令牌：OpenIddict 在兑换刷新令牌时
/// 本就会核对授权状态，这里再撤一遍令牌，是为了让撤销后的令牌在令牌表里也读得出「已撤销」。
/// </summary>
public class OidcGrantService : ApplicationService, IOidcGrantService
{
    private const string ClientNotFoundMessage = "Client not found.";

    /// <summary>
    /// 停用的客户端在同意页上与不存在的客户端同为 404，但错误码不同（<see cref="ErrorCodes.IDENTITY_OIDC_CLIENT_DISABLED"/>），
    /// 同意页据此说「这个应用已被停用」而不是「找不到」。
    /// </summary>
    private const string ClientDisabledMessage = "Client is disabled.";

    private readonly OpenIddictApplicationManager<OidcClient> _applications;
    private readonly OpenIddictAuthorizationManager<OidcAuthorization> _authorizations;
    private readonly OpenIddictTokenManager<OidcToken> _tokens;
    private readonly IRepository<OidcAuthorization, Guid> _repository;
    private readonly IUserDisplayNameProvider? _userDisplayNames;
    private readonly IUserEmailProvider? _userEmails;

    /// <summary>创建服务。</summary>
    /// <param name="serviceProvider">服务容器。</param>
    /// <param name="applications">OpenIddict 客户端管理器。</param>
    /// <param name="authorizations">OpenIddict 授权管理器。</param>
    /// <param name="tokens">OpenIddict 令牌管理器。</param>
    /// <param name="repository">授权仓储。</param>
    /// <param name="userDisplayNames">把管理端列表里的用户 Id 解析成名字（Identity 提供；缺席时名字留空）。</param>
    /// <param name="userEmails">把管理端列表里的用户 Id 解析成邮箱（Identity 提供；缺席时邮箱留空）。</param>
    public OidcGrantService(
        IServiceProvider serviceProvider,
        OpenIddictApplicationManager<OidcClient> applications,
        OpenIddictAuthorizationManager<OidcAuthorization> authorizations,
        OpenIddictTokenManager<OidcToken> tokens,
        IRepository<OidcAuthorization, Guid> repository,
        IUserDisplayNameProvider? userDisplayNames = null,
        IUserEmailProvider? userEmails = null)
        : base(serviceProvider)
    {
        _applications = Check.NotNull(applications);
        _authorizations = Check.NotNull(authorizations);
        _tokens = Check.NotNull(tokens);
        _repository = Check.NotNull(repository);
        _userDisplayNames = userDisplayNames;
        _userEmails = userEmails;
    }

    /// <inheritdoc />
    public virtual async Task<Result<List<OidcGrantDto>>> GetMyGrantsAsync(CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId();
        if (userId == null)
        {
            return Fail<List<OidcGrantDto>>("Authentication is required.", 401, ErrorCodes.UNAUTHORIZED);
        }

        var subject = userId.Value.ToString();
        var rows = await _repository.AsQueryable().AsNoTracking()
            .Include(a => a.Application)
            .Where(a => a.Subject == subject && a.Status == Statuses.Valid)
            .OrderByDescending(a => a.CreationDate)
            .ToListAsync(cancellationToken);

        return Ok(rows.Select(ToDto).ToList());
    }

    /// <inheritdoc />
    public virtual async Task<Result> RevokeMyGrantsAsync(string clientId, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(clientId);

        var userId = CurrentUserId();
        if (userId == null)
        {
            return Fail("Authentication is required.", 401, ErrorCodes.UNAUTHORIZED);
        }

        var revoked = await RevokeAsync(userId.Value, clientId, cancellationToken);
        return revoked < 0 ? Fail(ClientNotFoundMessage, 404, ErrorCodes.IDENTITY_OIDC_CLIENT_NOT_FOUND) : Ok();
    }

    /// <inheritdoc />
    public virtual async Task<Result<IPagedList<OidcGrantDto>>> GetPagedListAsync(OidcGrantQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var subject = query.UserId?.ToString();
        var filtered = _repository.AsQueryable().AsNoTracking()
            .Include(a => a.Application)
            .Where(a =>
                (subject == null || a.Subject == subject) &&
                (string.IsNullOrEmpty(query.ClientId) || a.Application!.ClientId == query.ClientId) &&
                (query.IncludeRevoked || a.Status == Statuses.Valid));

        var total = await filtered.CountAsync(cancellationToken);
        var rows = await filtered
            .OrderByDescending(a => a.CreationDate)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken);

        var users = await ResolveUsersAsync(rows, cancellationToken);
        var items = rows.Select(row => ToDto(row, users)).ToList();
        return Ok<IPagedList<OidcGrantDto>>(new PagedList<OidcGrantDto>(items, query.PageIndex, query.PageSize, total));
    }

    /// <inheritdoc />
    public virtual async Task<Result> RevokeAsync(Guid authorizationId, CancellationToken cancellationToken = default)
    {
        var authorization = await _authorizations.FindByIdAsync(authorizationId.ToString(), cancellationToken);
        if (authorization == null)
        {
            return Fail("Grant not found.", 404, ErrorCodes.IDENTITY_OIDC_GRANT_NOT_FOUND);
        }

        await _authorizations.TryRevokeAsync(authorization, cancellationToken);
        await _tokens.RevokeByAuthorizationIdAsync(authorizationId.ToString(), cancellationToken);
        return Ok();
    }

    /// <inheritdoc />
    /// <remarks>客户端不存在时返回 -1。</remarks>
    public virtual async Task<int> RevokeAsync(Guid userId, string clientId, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(clientId);

        var client = await _applications.FindByClientIdAsync(clientId, cancellationToken);
        if (client == null)
        {
            return -1;
        }

        var subject = userId.ToString();
        var applicationId = client.Id.ToString();
        var revoked = await _authorizations.RevokeAsync(subject, applicationId, status: null, type: null, cancellationToken);
        await _tokens.RevokeAsync(subject, applicationId, status: null, type: null, cancellationToken);
        return (int)revoked;
    }

    /// <inheritdoc />
    public virtual async Task<Result<OidcConsentInfoDto>> GetConsentInfoAsync(string clientId, string? scope, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(clientId);

        var userId = CurrentUserId();
        if (userId == null)
        {
            return Fail<OidcConsentInfoDto>("Authentication is required.", 401, ErrorCodes.UNAUTHORIZED);
        }

        var client = await _applications.FindByClientIdAsync(clientId, cancellationToken);
        if (client == null)
        {
            return Fail<OidcConsentInfoDto>(ClientNotFoundMessage, 404, ErrorCodes.IDENTITY_OIDC_CLIENT_NOT_FOUND);
        }

        if (!client.IsEnabled)
        {
            return Fail<OidcConsentInfoDto>(ClientDisabledMessage, 404, ErrorCodes.IDENTITY_OIDC_CLIENT_DISABLED);
        }

        var scopes = await PermittedScopesAsync(client, (scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries), cancellationToken);
        var granted = await HasPermanentGrantAsync(userId.Value, client, scopes, cancellationToken);

        return Ok(new OidcConsentInfoDto
        {
            ClientId = clientId,
            DisplayName = client.DisplayName,
            Description = client.Description,
            Scopes = scopes.ToList(),
            AlreadyGranted = granted,
        });
    }

    /// <inheritdoc />
    public virtual async Task<Result> GrantConsentAsync(GrantOidcConsentDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        Check.NotNullOrWhiteSpace(input.ClientId);

        var userId = CurrentUserId();
        if (userId == null)
        {
            return Fail("Authentication is required.", 401, ErrorCodes.UNAUTHORIZED);
        }

        var client = await _applications.FindByClientIdAsync(input.ClientId, cancellationToken);
        if (client == null)
        {
            return Fail(ClientNotFoundMessage, 404, ErrorCodes.IDENTITY_OIDC_CLIENT_NOT_FOUND);
        }

        if (!client.IsEnabled)
        {
            return Fail(ClientDisabledMessage, 404, ErrorCodes.IDENTITY_OIDC_CLIENT_DISABLED);
        }

        var requested = (input.Scopes ?? []).SelectMany(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var scopes = await PermittedScopesAsync(client, requested, cancellationToken);
        if (await HasPermanentGrantAsync(userId.Value, client, scopes, cancellationToken))
        {
            return Ok();
        }

        var subject = userId.Value.ToString();
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, subject);
        identity.SetScopes(scopes);

        await _authorizations.CreateAsync(identity, subject, client.Id.ToString(), AuthorizationTypes.Permanent, scopes, cancellationToken);
        return Ok();
    }

    /// <summary>
    /// 只保留客户端被允许的作用域；<c>openid</c> 恒保留。
    /// </summary>
    protected virtual async Task<ImmutableArray<string>> PermittedScopesAsync(OidcClient client, IEnumerable<string> requested, CancellationToken cancellationToken)
    {
        var permitted = new List<string> { Scopes.OpenId };
        foreach (var scope in requested.Distinct(StringComparer.Ordinal))
        {
            if (scope != Scopes.OpenId && await _applications.HasPermissionAsync(client, ClientPermissions.Prefixes.Scope + scope, cancellationToken))
            {
                permitted.Add(scope);
            }
        }

        return [.. permitted];
    }

    private async Task<bool> HasPermanentGrantAsync(Guid userId, OidcClient client, ImmutableArray<string> scopes, CancellationToken cancellationToken)
    {
        await foreach (var _ in _authorizations.FindAsync(
            userId.ToString(), client.Id.ToString(), Statuses.Valid, AuthorizationTypes.Permanent, scopes, cancellationToken))
        {
            return true;
        }

        return false;
    }

    private Guid? CurrentUserId() => CurrentUser?.Id;

    /// <summary>
    /// 一页授权背后的人：整页一次批量解析名字与邮箱（两次查询），不逐行往返。
    /// 解析不到的用户不在结果里，呈现端回落到用户 Id。
    /// </summary>
    private async Task<GrantUsers> ResolveUsersAsync(IReadOnlyCollection<OidcAuthorization> rows, CancellationToken cancellationToken)
    {
        var userIds = rows.Select(row => ParseUserId(row.Subject)).Where(id => id != Guid.Empty).Distinct().ToList();
        if (userIds.Count == 0)
        {
            return GrantUsers.None;
        }

        var names = _userDisplayNames == null
            ? GrantUsers.None.Names
            : await _userDisplayNames.GetDisplayNamesAsync(userIds, cancellationToken);
        var emails = _userEmails == null
            ? GrantUsers.None.Emails
            : await _userEmails.GetEmailsAsync(userIds, cancellationToken);
        return new GrantUsers(names, emails);
    }

    private static Guid ParseUserId(string? subject) => Guid.TryParse(subject, out var userId) ? userId : Guid.Empty;

    private static OidcGrantDto ToDto(OidcAuthorization authorization) => ToDto(authorization, GrantUsers.None);

    private static OidcGrantDto ToDto(OidcAuthorization authorization, GrantUsers users)
    {
        var userId = ParseUserId(authorization.Subject);
        return new OidcGrantDto
        {
            Id = authorization.Id,
            UserId = userId,
            UserDisplayName = users.Names.GetValueOrDefault(userId),
            UserEmail = users.Emails.GetValueOrDefault(userId),
            ClientId = authorization.Application?.ClientId ?? string.Empty,
            ClientDisplayName = authorization.Application?.DisplayName,
            Scopes = string.IsNullOrEmpty(authorization.Scopes) ? [] : JsonSerializer.Deserialize<List<string>>(authorization.Scopes) ?? [],
            Status = authorization.Status,
            CreationDate = authorization.CreationDate,
        };
    }

    /// <summary>按用户 Id 解析出的名字与邮箱。</summary>
    private sealed record GrantUsers(IReadOnlyDictionary<Guid, string> Names, IReadOnlyDictionary<Guid, string> Emails)
    {
        public static readonly GrantUsers None = new(new Dictionary<Guid, string>(), new Dictionary<Guid, string>());
    }
}
