namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// Default admin feature value controller.
/// Route: /api/admin/feature-values (auto-adds /api prefix)
/// </summary>
[Route("admin/feature-values")]
public class DefaultAdminFeatureValueController : FeatureValueAdminControllerBase
{
    public DefaultAdminFeatureValueController(IFeatureService featureService)
        : base(featureService)
    {
    }
}
