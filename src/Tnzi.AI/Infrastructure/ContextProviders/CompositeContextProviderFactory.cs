namespace Tnzi.AI.Infrastructure.ContextProviders;

/// <summary>
/// Builds a <see cref="CompositeContextProvider"/> per request from registered
/// <see cref="IContextProviderContributor"/> instances. Pre-sorts contributors and caches the
/// composite logger at construction so the hot path (one call per agent invocation) avoids
/// repeated <c>OrderBy</c> and <c>ILoggerFactory.CreateLogger</c> work.
/// </summary>
public sealed class CompositeContextProviderFactory
{
    private readonly IContextProviderContributor[] _orderedContributors;
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ILogger<CompositeContextProvider> _compositeLogger;
    private readonly ILogger<CompositeContextProviderFactory> _logger;

    public CompositeContextProviderFactory(
        IEnumerable<IContextProviderContributor> contributors,
        IOptionsMonitor<AIOptions> options,
        ITokenEstimator tokenEstimator,
        ILoggerFactory loggerFactory,
        ILogger<CompositeContextProviderFactory> logger)
    {
        Check.NotNull(contributors);
        Check.NotNull(loggerFactory);
        _orderedContributors = contributors.OrderBy(c => c.Order).ToArray();
        _options = Check.NotNull(options);
        _tokenEstimator = Check.NotNull(tokenEstimator);
        _compositeLogger = loggerFactory.CreateLogger<CompositeContextProvider>();
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// Builds a CompositeContextProvider for the given creation context. Returns null when
    /// context providers are globally disabled or no contributor produced a provider.
    /// </summary>
    public CompositeContextProvider? TryBuild(ContextProviderCreationContext creationContext)
    {
        Check.NotNull(creationContext);

        if (!_options.CurrentValue.ContextProviders.Enabled)
        {
            return null;
        }

        var composite = new CompositeContextProvider(_compositeLogger, _options, _tokenEstimator);

        foreach (var contributor in _orderedContributors)
        {
            try
            {
                var provider = contributor.TryCreate(creationContext);
                if (provider != null)
                {
                    composite.AddProvider(provider);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Context provider contributor {ContributorType} failed", contributor.GetType().Name);
            }
        }

        return composite.ProviderCount == 0 ? null : composite;
    }
}
