
namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员文件存储控制器
/// 路由：/api/admin/files（自动添加 /api 前缀）
/// 用户项目可以实现 AdminStorageController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/files")]
public class DefaultAdminStorageController : StorageAdminControllerBase
{
    public DefaultAdminStorageController(
        IFileStorageService fileStorageService,
        IFileReferenceService fileReferenceService)
        : base(fileStorageService, fileReferenceService)
    {
    }
}
