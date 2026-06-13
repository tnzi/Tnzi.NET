
namespace Tnzi.AI.Infrastructure.ContextProviders.Contributors;

/// <summary>
/// RAG 文本搜索上下文贡献者
/// </summary>
internal sealed class TextSearchContributor : IContextProviderContributor
{
    private readonly ITextSearchService _textSearchService;
    private readonly IOptions<AIOptions> _options;
    private readonly ILoggerFactory _loggerFactory;

    public int Order => ContextProviderOrders.Rag;

    public TextSearchContributor(
        ITextSearchService textSearchService,
        IOptions<AIOptions> options,
        ILoggerFactory loggerFactory)
    {
        _textSearchService = Check.NotNull(textSearchService);
        _options = Check.NotNull(options);
        _loggerFactory = Check.NotNull(loggerFactory);
    }

    public IContextProvider? TryCreate(ContextProviderCreationContext context)
    {
        if (!_options.Value.ContextProviders.TextSearch.Enabled) return null;

        try
        {
            var logger = _loggerFactory.CreateLogger<TextSearchProvider>();
            return new TextSearchProvider(_textSearchService, _options.Value.ContextProviders.TextSearch, logger, context.KnowledgeBaseIds);
        }
        catch (Exception ex)
        {
            _loggerFactory.CreateLogger<TextSearchContributor>()
                .LogError(ex, "Failed to create TextSearchProvider");
            return null;
        }
    }
}
