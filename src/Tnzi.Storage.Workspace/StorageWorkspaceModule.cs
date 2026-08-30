namespace Tnzi.Storage.Workspace;

/// <summary>
/// 文件工作区子模块：把「人在界面上拿文件做的事」从「程序化的对象仓库」里分出来。
/// </summary>
/// <remarks>
/// <para>
/// <b>业务范围</b>：浏览目录树、发一条分享链接、留住历史版本、断点续传一个大文件。
/// 与之相对的是父模块 <c>Tnzi.Storage</c> 那一半 —— 别的模块往里写字节、按 id 取回字节、
/// 靠 <c>[FileField]</c> 数引用、到期回收。两半的分界不是「功能大小」而是<b>谁在用</b>：
/// 前者的用户是坐在管理端前面的人，后者的用户是别的代码。
/// </para>
/// <para>
/// <b>谁会刻意只加载父模块</b>：任何把存储当对象仓库用的宿主。框架自带的六个消费方
/// （聊天附件、财务单据与收据、发票、签署原件与成品、AI 收据识别）<b>无一例外</b>只用
/// <c>IFileStorageService</c> 与 <c>IFileReferenceAccessResolver</c> —— 目录、分享、版本、
/// 分片会话一个都不碰。一个只做这些事的部署因此少建 5 张表、少 36 个端点。
/// </para>
/// <para>
/// <b>缺席时退化成什么</b>：
/// <list type="bullet">
/// <item>目录树的两个控制器（<c>storage/folders</c> 与 <c>admin/storage/folders</c>）与审计控制器
///   （<c>admin/storage/audit</c>）<b>整个不注册</b>，路由不存在，请求得到 404。</item>
/// <item>分享 / 版本 / 分片上传的端点<b>仍在原路由上</b>（它们长在父模块留下来的
///   <c>DefaultStorageController</c> 与 <c>DefaultStorageAdminController</c> 上），
///   但对应的服务解析不到，一律返回 <b>501</b> 并指名要加载的包。URL 一个字不变。</item>
/// <item><c>FileRecord.FolderId</c> 这一列<b>留在父模块</b>并照常出现在每个 <c>FileRecordDto</c> 上：
///   写它的是本模块，读它的是父模块的查询。没有本模块时它恒为 null —— 少一个可用的过滤维度，
///   不是一个坏掉的字段。</item>
/// <item>后台清理少跑「过期分片会话 + 残留分片」那一趟（本模块以
///   <c>IStorageCleanupContributor</c> 身份挂进去）。父模块自己的三趟不受影响。</item>
/// <item>★ <b>一处已知的表里不一</b>：<c>ShareOptions</c> 这个类型按约定留在父模块
///   （它是 <c>StorageOptions.Share</c> 的嵌套属性，配置节路径 <c>Storage:Share</c> 不能变），
///   而配置中心的分组定义是从<b>已加载模块的程序集</b>扫出来的 —— 于是没有本模块的宿主，
///   系统设置里<b>仍会渲染出一个 "Share Links" 分组</b>，改它不影响任何行为（没有分享链接可发）。
///   刻意不把这五个设置在子模块里再抄一份来消除它：手抄的清单会一条一条地漂，
///   代价高于一个不会造成错误行为的空分组。</item>
/// </list>
/// </para>
/// <para>
/// <b>表名一个字不变</b>：<see cref="TableNamePrefix"/> 与父模块<b>逐字相同</b>。
/// 前缀是按<b>实体所在程序集</b>回查模块拿到的（见 <c>TableNamePrefixConfiguration</c>），
/// 实体换了程序集就得由新程序集的模块把同一个前缀再声明一遍。漏掉这一行，
/// 五张表会静默地丢掉 <c>Storage_</c> 前缀 —— 编译照过、测试照绿、迁移里多出五张空表。
/// </para>
/// </remarks>
[DependsOn(typeof(StorageModule))]
public class StorageWorkspaceModule : TnziApplicationModule
{
    /// <summary>Storage(30) 与 Storage.Cloud(31) 之后；实际次序由 <c>[DependsOn]</c> 拓扑排序保证。</summary>
    public override int LoadOrder => 32;

    /// <summary>
    /// 表名前缀，<b>与父模块逐字相同</b>。见类注释「表名一个字不变」。
    /// </summary>
    public override string? TableNamePrefix => "Storage";

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // 四个服务。父模块声明了其中三个契约（分享 / 版本 / 分片上传）并可选注入它们，
        // 因为那批端点留在父模块的控制器上；IFileFolderService 的契约随实现一起在本模块，
        // 父模块没有任何代码需要它。
        services.AddScoped<IFileFolderService, FileFolderService>();
        services.AddScoped<IFileShareService, FileShareService>();
        services.AddScoped<IFileVersionService, FileVersionService>();
        services.AddScoped<IFileChunkUploadService, FileChunkUploadService>();

        // 把「过期分片会话 + 残留分片」这一趟挂进父模块的后台清理。
        // 父模块的 FileCleanupService 以 IEnumerable<T> 注入贡献者，没有本模块时是空集合。
        services.AddScoped<IStorageCleanupContributor, ExpiredUploadSessionCleanupContributor>();

        return Task.CompletedTask;
    }
}
