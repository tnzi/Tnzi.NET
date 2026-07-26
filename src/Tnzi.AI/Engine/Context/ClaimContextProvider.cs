
namespace Tnzi.AI.Engine.Context;

/// <summary>
/// Contributes AI conversation context from JWT claims on the current HTTP request.
/// Matches claims by a configurable prefix (default "ai:") and formats them using
/// a template, e.g. "User preferences: ai:locale=zh-CN, ai:tone=formal".
/// </summary>
/// <remarks>
/// Implements both <see cref="IContextProviderContributor"/> (registration) and
/// <see cref="IContextProvider"/> (injection). <see cref="TryCreate"/> returns
/// <c>this</c> because claim reading is stateless and bound to the current
/// <see cref="IHttpContextAccessor"/> - no per-agent-session state is needed.
/// </remarks>
public sealed class ClaimContextProvider : IContextProviderContributor, IContextProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptions<ClaimInjectionOptions> _options;

    /// <inheritdoc />
    public int Order => ContextProviderOrders.Custom;

    public ClaimContextProvider(
        IHttpContextAccessor httpContextAccessor,
        IOptions<ClaimInjectionOptions> options)
    {
        _httpContextAccessor = Check.NotNull(httpContextAccessor);
        _options = Check.NotNull(options);
    }

    /// <inheritdoc />
    public IContextProvider? TryCreate(ContextProviderCreationContext context) => this;

    /// <inheritdoc />
    public Task<ContextInjection> GetContextAsync(
        List<ChatMessage> messages,
        CancellationToken ct = default)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return Task.FromResult(ContextInjection.Empty);

        var opt = _options.Value;
        var prefix = opt.ClaimPrefix ?? string.Empty;
        var includedSet = opt.IncludedClaims;

        var relevant = user.Claims
            .Where(c =>
                (!string.IsNullOrEmpty(prefix) && c.Type.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
                (includedSet != null && includedSet.Contains(c.Type)))
            .ToList();

        if (relevant.Count == 0)
            return Task.FromResult(ContextInjection.Empty);

        var formatted = string.Join(", ", relevant.Select(c => $"{c.Type}={c.Value}"));
        var text = opt.TemplateFormat.Replace("{claims}", formatted, StringComparison.Ordinal);

        var injection = new ContextInjection
        {
            Messages = [new ChatMessage(ChatRole.System, text)]
        };

        return Task.FromResult(injection);
    }
}
