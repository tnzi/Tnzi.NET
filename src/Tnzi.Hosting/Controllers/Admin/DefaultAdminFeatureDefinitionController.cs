namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// Default admin feature definition controller.
/// Route: /api/admin/feature-definitions (auto-adds /api prefix)
/// </summary>
[Route("admin/feature-definitions")]
public class DefaultAdminFeatureDefinitionController : FeatureDefinitionAdminControllerBase
{
    public DefaultAdminFeatureDefinitionController(IFeatureService featureService)
        : base(featureService)
    {
    }
}
