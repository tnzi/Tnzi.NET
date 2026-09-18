using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Tnzi.Security.Authorization;
using Tnzi.TestBase;

namespace Tnzi.AspNetCore.Tests.Mvc;

/// <summary>
/// 反射约定门禁：每个继承 <see cref="ApiAdminControllerBase"/> 的控制器上的写端点
/// (POST/PUT/PATCH/DELETE) 都必须携带方法级 <c>[ApiAuthorize(PermissionName=...)]</c>
/// (.create / .update / .delete / .execute / .assign 类操作码)。
///
/// 这是 admin 三层 AND 门（认证边界 ∧ 类级 .view ∧ 方法级操作码）在端点级强制的
/// 全量静态孪生：<c>AdminGateCompositionTests</c>（Tnzi.Authorization.Tests）是代表性
/// 样本枚举，本测试扫描所有框架程序集，防止新增写端点漏标方法级操作码而行为测试
/// （mock 服务层）依旧全绿。
///
/// 程序集来源：测试输出目录里的全部 <c>Tnzi*.dll</c>，也就是<b>本测试项目引用到的</b>那些
/// （不依赖模块能否在最小测试配置下成功加载/配置）。
///
/// ★★ <b>这段注释此前是错的，而错法很贵。</b>它曾写着「经 Tnzi.Hosting 的 ProjectReference
/// 传递引用全部业务模块」—— 但 Tnzi.Hosting 对那些模块一律用 <c>PrivateAssets="all"</c>，
/// 引用<b>不向外流</b>。实测 2026-09-02：36 个带 admin 控制器的程序集里只有 27 个进了 bin，
/// 9 个（08-29 拆出来的子模块为主）完全在扫描面之外 —— 它们新增写端点漏标操作码时，
/// 这道「唯一的静态门禁」一声不响。九个引用已补进 csproj，覆盖面本身改由
/// <see cref="Scan_CoversEveryAssemblyThatHasAnAdminController"/> 守着。
///
/// 允许显式豁免清单（<see cref="Allowlist"/>）：修复某个存量违规的权限码是另一个决策，
/// 不在本约定测试内顺手改业务控制器；扫出的存量违规列入豁免清单并在提交说明中完整列出。
/// </summary>
public class AdminWriteEndpointPermissionConventionTests
{
    private static readonly string[] WriteVerbs = ["POST", "PUT", "PATCH", "DELETE"];

    /// <summary>
    /// 存量违规豁免清单。键格式：<c>{Controller.FullName}.{Method}:{Verbs}</c>。
    /// </summary>
    /// <summary>
    /// 存量违规豁免清单。键格式：<c>{Controller.FullName}.{Method}:{Verbs}</c>。
    /// 本轮全量扫描共 41 项，两类：
    /// <list type="number">
    ///   <item><b>真正的写操作缺码（2）</b>：DELETE 语义的变更端点漏方法级操作码，是存量遗漏。
    ///     本任务不改业务/诊断控制器权限码（另一个决策），列此豁免并在报告中标注。</item>
    ///   <item><b>POST-读（39）</b>：以 POST 承载查询体的读端点（GetList/Query/Search/Export/
    ///     Preview/Validate/GetSummary/GetTrend/Evaluate/Wait/Detect/Check 等），不改数据，
    ///     仅由类级 <c>.view</c> 把守——按现有约定无需写动作码。列此豁免以承认其非"写端点"。</item>
    /// </list>
    /// 新增的写动作端点（POST-create / PUT / PATCH / DELETE 变更）不在清单内 → 必须补方法级码，
    /// 否则本门禁失败；确属读端点则需显式加入本清单并说明理由。
    /// </summary>
    private static readonly HashSet<string> Allowlist = new(StringComparer.Ordinal)
    {
        // ── (0) 服务层动态授权：配置中心跨多模块，写授权码依赖运行时 groupKey
        //    （{group}.settings.{slug}.update），无法用静态方法级特性表达，改由
        //    SettingsCenterService 按组强制（超管 bypass）。见控制器类注释。 ──
        // ── 2026-09-02 扫描面从 27 个程序集扩到 36 个后新暴露的存量违规（1 项）──
        //    清空全部性能采样，只由类级 system.performance.view 把守。它确实是写动作，
        //    但补码是**另一个决策**且是单向的授权收紧：目前持有 .view 的人现在就能清，
        //    加一个 .delete 会让他们当场失去这个能力直到被重新授予。按本门禁自己的约定
        //    （存量违规列入豁免并在提交说明中列出）先记在这里，不在约定测试里顺手改业务控制器。
        "Tnzi.Performance.Controllers.DefaultPerformanceAdminController.Clear:DELETE",
        "Tnzi.System.Controllers.Admin.DefaultSettingsCenterAdminController.SaveGroup:PUT",
        "Tnzi.System.Controllers.Admin.DefaultSettingsCenterAdminController.ResetGroup:DELETE",
        //    单据讨论删除：作者删自己那条无需任何权限码（谁都可能写错一句），删他人
        //    的才要 finance.comment.delete。静态方法级门会把作者本人也一并挡掉，
        //    所以判定放在 DocumentCommentService 里（Authorization 未加载时按"没有
        //    该权限"处理，只减权不增权）。
        "Tnzi.Finance.Controllers.Admin.DefaultFinanceDocumentCollaborationAdminController.DeleteComment:DELETE",
        //    双人授权撤回：只能撤自己发起、且尚未被决定的那一条（DualControlService.CancelAsync
        //    比对 RequesterId，不是本人一律 403）。与上面那条同构 —— 静态方法级门会把发起人
        //    本人也挡掉，而「撤回自己刚发起的请求」不该需要审批权。别人要让它不生效走拒绝，
        //    那条挂着 authorization.dualControl.approve 并且会留下是谁拒的。
        "Tnzi.Authorization.Controllers.Admin.DefaultDualControlAdminController.Cancel:POST",

        // ── (1) 真正的写操作缺码：已于 2026-07-07 补齐方法级操作码
        //    (system.diagnostics.execute / system.signalr.execute)，本区清空 ──

        // ── (2) POST-读：带查询体的读端点，仅类级 .view 把守，无数据变更 ──
        // 重估 preview:纯计算预览不落库(落库的 run 有 finance.revaluation.execute);
        // 余额汇总 verify:只读诊断对账(会重建的 rebuild 有 finance.balanceSummary.execute);
        // 科目余额 balances:科目集经请求体传递(一页科目的 GUID 列表超 URL 长度上限)的纯读聚合。
        "Tnzi.Finance.Controllers.Admin.DefaultFinanceRevaluationAdminController.Preview:POST",
        "Tnzi.Finance.Controllers.Admin.DefaultFinanceBalanceSummaryAdminController.Verify:POST",
        "Tnzi.Finance.Controllers.Admin.DefaultFinanceAccountAdminController.GetBalances:POST",
        // 支票 preview 曾列在这里（零副作用）。2026-09-12 移出：它渲染完整票面，白纸票纸下
        // 磁码行是解密后的真账号，产物与 {id}/render 同属可流通级，现带 finance.check.create 写码。
        // 周期性单据 PreviewSchedule:排期推演纯计算,不落库也不造单据(真正生成的
        // run/run-due 带 finance.recurring.execute)。POST 仅因入参是整个模板草案
        // ——锚点 31 号 x 每季度这类规则在脑子里算不清楚,让人先看见日期再保存。
        "Tnzi.Finance.Recurring.Controllers.Admin.DefaultFinanceRecurringAdminController.PreviewSchedule:POST",
        // 银行规则 test:试跑，经求值器对未对账流水求值后返回命中样本。求值契约明确
        // 要求无副作用（试跑功能正建立在这一点上），不写库、不建单、不改流水状态。
        // POST 仅因入参是一组筛选条件（账户 + 样本数）。
        "Tnzi.Finance.Banking.Controllers.Admin.DefaultFinanceBankRuleAdminController.Test:POST",
        "Tnzi.AI.Controllers.Admin.DefaultAgentAdminController.GetList:POST",
        "Tnzi.AI.Controllers.Admin.DefaultAgentAdminController.GetVersions:POST",
        "Tnzi.AI.Controllers.Admin.DefaultAgentAdminController.Validate:POST",
        "Tnzi.AI.Controllers.Admin.DefaultAgentMemoryAdminController.GetList:POST",
        "Tnzi.AI.Controllers.Admin.DefaultAgentRunAdminController.GetList:POST",
        "Tnzi.AI.Controllers.Admin.DefaultAgentRunAdminController.Wait:POST",
        "Tnzi.AI.Controllers.Admin.DefaultEvaluationAdminController.GetList:POST",
        "Tnzi.AI.Controllers.Admin.DefaultMcpClientAdminController.GetList:POST",
        "Tnzi.AI.Controllers.Admin.DefaultPermissionAdminController.Evaluate:POST",
        "Tnzi.AI.Controllers.Admin.DefaultProviderAdminController.GetList:POST",
        "Tnzi.AI.Controllers.Admin.DefaultQuotaAdminController.GetList:POST",
        "Tnzi.AI.Controllers.Admin.DefaultThreadAdminController.GetList:POST",
        "Tnzi.AI.Controllers.Admin.DefaultUsageAnalyticsAdminController.GetSummary:POST",
        "Tnzi.AI.Controllers.Admin.DefaultUsageAnalyticsAdminController.GetLogs:POST",
        "Tnzi.AI.Controllers.Admin.DefaultUsageAnalyticsAdminController.GetTrend:POST",
        "Tnzi.AI.Workflow.Controllers.Admin.DefaultWorkflowAdminController.GetList:POST",
        "Tnzi.AI.Workflow.Controllers.Admin.DefaultWorkflowAdminController.Validate:POST",
        "Tnzi.AI.Rag.Controllers.Admin.DefaultKnowledgeBaseAdminController.GetList:POST",
        "Tnzi.AI.Rag.Controllers.Admin.DefaultKnowledgeBaseAdminController.GetDocuments:POST",
        "Tnzi.AI.Rag.Controllers.Admin.DefaultKnowledgeBaseAdminController.Search:POST",
        "Tnzi.AI.Rag.Controllers.Admin.DefaultKnowledgeBaseAdminController.SearchAll:POST",
        "Tnzi.Audit.Controllers.Admin.DefaultAuditOperationAdminController.GetList:POST",
        //    销毁证明查询：只读证明列表，不触发任何销毁（触发是同控制器的 Run，它带
        //    audit.destruction.execute）。
        "Tnzi.Audit.Controllers.Admin.DefaultDataDestructionAdminController.GetList:POST",
        //    记录访问查询：只读审计条目。它自身刻意不做记录级登记，否则每翻一页都会
        //    往被查记录的访问历史里插进一条无关的行。
        "Tnzi.Audit.Controllers.Admin.DefaultRecordAccessAdminController.GetList:POST",
        "Tnzi.Audit.Controllers.Admin.DefaultAuditOperationAdminController.ExportCsv:POST",
        "Tnzi.Audit.Controllers.Admin.DefaultAuditOperationAdminController.ExportJson:POST",
        "Tnzi.Authorization.DataAuth.Controllers.Admin.DefaultDataAuthAdminController.CheckDataPermission:POST",
        "Tnzi.Chat.Controllers.Admin.DefaultChatAdminController.QueryConversations:POST",
        "Tnzi.Identity.Controllers.Admin.DefaultLoginLogAdminController.GetList:POST",
        "Tnzi.Identity.Controllers.Admin.DefaultLoginSecurityAdminController.DetectAbnormalLogin:POST",
        "Tnzi.Identity.Controllers.Admin.DefaultUserAdminController.GetList:POST",
        "Tnzi.Identity.Controllers.Admin.DefaultUserAdminController.ExportCsv:POST",
        "Tnzi.Notification.Controllers.Admin.DefaultNotificationAdminController.Query:POST",
        "Tnzi.Notification.Controllers.Admin.DefaultNotificationAdminController.GetScheduled:POST",
        "Tnzi.Notification.Controllers.Admin.DefaultNotificationAdminController.Preview:POST",
        "Tnzi.Storage.Controllers.Admin.DefaultStorageAdminController.QueryFiles:POST",
        "Tnzi.Storage.Controllers.Admin.DefaultStorageAdminController.BatchVerifyIntegrity:POST",
        "Tnzi.Storage.Controllers.Admin.DefaultStorageAdminController.QueryActiveShares:POST",
    };

    [Fact]
    public void Every_admin_write_endpoint_declares_a_method_level_permission_code()
    {
        var adminControllers = LoadFrameworkAssemblies()
            .SelectMany(SafeGetTypes)
            .Where(t => typeof(ApiAdminControllerBase).IsAssignableFrom(t)
                        && !t.IsAbstract
                        && t != typeof(ApiAdminControllerBase))
            .Distinct()
            .ToList();

        // 非空洞守卫：若程序集未加载/类型扫描失败导致 adminControllers 偏少，
        // 约定门会退化为恒真的假绿。此处锁定实际扫到了成规模的 admin 控制器。
        Assert.True(
            adminControllers.Count >= 30,
            $"Expected the scan to discover a substantial set of admin controllers, but found only {adminControllers.Count}. " +
            "The module assemblies may not be loaded; the convention gate would vacuously pass.");

        var violations = new List<string>();

        foreach (var controller in adminControllers.OrderBy(c => c.FullName, StringComparer.Ordinal))
        {
            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.DeclaringType == typeof(object)) continue;
                if (method.GetCustomAttribute<NonActionAttribute>() != null) continue;

                var writeVerbs = method
                    .GetCustomAttributes<HttpMethodAttribute>(inherit: true)
                    .SelectMany(a => a.HttpMethods)
                    .Select(v => v.ToUpperInvariant())
                    .Where(v => WriteVerbs.Contains(v))
                    .Distinct()
                    .OrderBy(v => v, StringComparer.Ordinal)
                    .ToList();
                if (writeVerbs.Count == 0) continue;

                // [AllowAnonymous] 完全豁免授权（当前无此写端点，防御性保留）
                if (method.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) != null) continue;

                var hasMethodLevelCode = method
                    .GetCustomAttributes<ApiAuthorizeAttribute>(inherit: true)
                    .Any(a => !string.IsNullOrEmpty(a.PermissionName));
                if (hasMethodLevelCode) continue;

                var key = $"{controller.FullName}.{method.Name}:{string.Join("/", writeVerbs)}";
                if (!Allowlist.Contains(key))
                    violations.Add(key);
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} admin write endpoint(s) missing a method-level " +
            "[ApiAuthorize(PermissionName=...)] operation code (add the code, or allowlist with justification):" +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => "  - " + v)));
    }

    /// <summary>
    /// ★ 守着<b>扫描面本身</b>：每个在 <c>src/</c> 里带 admin 控制器的程序集都必须进得了 bin。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 上面那道门禁只对它<b>加载得到</b>的程序集有效，而加载得到什么完全由本测试项目的
    /// csproj 引用决定 —— 少一行引用，那个模块的全部写端点就静默退出扫描面，
    /// 门禁照样全绿。这正是 2026-09-02 实测出来的形态：9 个程序集在外面。
    /// </para>
    /// <para>
    /// ★ <b>判据扫的是源码目录，不是已加载的程序集。</b>新模块连 csproj 引用都还没加时，
    /// 它的 DLL 根本不在进程里 —— 拿已加载集合当分母，会把「漏加」读成「没有漏加」。
    /// 同 <c>ModuleInventoryTests</c> 扫 <c>src/*Module.cs</c> 源码的理由。
    /// </para>
    /// </remarks>
    [Fact]
    public void Scan_CoversEveryAssemblyThatHasAnAdminController()
    {
        var repoRoot = RepoRoot.Locate();
        var srcRoot = Path.Combine(repoRoot, "src");

        // ★ 走 RepoScan 而不是 SearchOption.AllDirectories：Tnzi* 这个通配也命中 src/Tnzi.UI，
        // 那是个 pnpm 工作区，裸递归会跟着 node_modules 的 junction 走进无穷路径，
        // 测试宿主卡死且一条结果都打不出来。bin/obj 的剔除也一并交给它。
        var expected = Directory
            .GetDirectories(srcRoot, "Tnzi*")
            .Select(dir => new DirectoryInfo(dir).Name)
            .Where(name => RepoScan.EnumerateFiles($"src/{name}", "*AdminController.cs").Any())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var loaded = LoadFrameworkAssemblies()
            .Select(a => a.GetName().Name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = expected.Where(name => !loaded.Contains(name)).ToList();

        Assert.True(missing.Count == 0,
            $"{missing.Count} assembly/assemblies ship an admin controller but are outside this gate's scan surface. "
            + "Their write endpoints can drop a method-level permission code and this test stays green. "
            + "Add a ProjectReference in Tnzi.AspNetCore.Tests.csproj:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, missing.Select(m => "  - " + m)));
    }

    /// <summary>
    /// 加载测试输出目录里全部 <c>Tnzi*.dll</c>（框架 + 全部业务模块程序集）。
    /// </summary>
    private static IReadOnlyList<Assembly> LoadFrameworkAssemblies()
    {
        var loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "Tnzi*.dll"))
        {
            var simpleName = Path.GetFileNameWithoutExtension(dll);
            if (loaded.ContainsKey(simpleName)) continue;
            try
            {
                loaded[simpleName] = Assembly.Load(AssemblyName.GetAssemblyName(dll));
            }
            catch
            {
                // 非托管/无法加载的 DLL 跳过
            }
        }

        return loaded.Values.ToList();
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}
