
namespace Tnzi.AI.Infrastructure.ContextProviders.Contributors;

/// <summary>
/// 项目上下文贡献者
/// </summary>
internal sealed class ProjectContextContributor : IContextProviderContributor
{
    private readonly ProjectContextProvider _projectContextProvider;
    private readonly IOptions<AIOptions> _options;

    public int Order => ContextProviderOrders.Custom - 10;

    public ProjectContextContributor(
        ProjectContextProvider projectContextProvider,
        IOptions<AIOptions> options)
    {
        _projectContextProvider = Check.NotNull(projectContextProvider);
        _options = Check.NotNull(options);
    }

    public IContextProvider? TryCreate(ContextProviderCreationContext context)
    {
        if (!_options.Value.ContextProviders.ProjectContext.Enabled) return null;
        return _projectContextProvider;
    }
}
