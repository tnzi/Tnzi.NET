namespace Tnzi.Feature.Services;

/// <summary>
/// Code-level feature definition provider.
/// Implement this interface to register feature definitions in code.
/// </summary>
public interface IFeatureDefinitionProvider
{
    /// <summary>
    /// Define features using the provided context
    /// </summary>
    /// <param name="context">Feature definition context</param>
    void Define(IFeatureDefinitionContext context);
}
