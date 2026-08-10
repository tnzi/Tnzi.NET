using System.Text;
using System.Text.RegularExpressions;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 从 <c>@tnzi/core</c> 的 <c>services/*/api.ts</c> 里读出「前端实际会请求哪些端点」。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由：<c>packages/core/src/services/</c> 是<b>手工维护</b>的（文件里没有任何
/// AUTO-GENERATED 标记，注释是人写的「Aligned with backend DefaultStorageController」），
/// 所以后端改了路由、前端没跟，编译器不会说话、测试也不会红 —— 症状只会在运行时以 404 出现。
/// 仓库里曾有一条 CI 步骤声称检查这件事（<c>codegen:check</c>），但它被写成
/// <c>if [ -f src/Tnzi.UI/openapi.json ]</c> 而那个文件从不存在，于是**永远打印 skip**；
/// 更下一层，<c>tnzi generate</c> 还要求仓库里有 <c>tnzi.json</c>，那个也不存在。
/// 这个扫描器 + <see cref="FrontendBackendContractTests"/> 是那步假门禁的替代品。
/// </para>
/// <para>
/// 正则而非 TypeScript AST：测试跑在 .NET 侧（这里是唯一能<b>反射</b>到全部 145 个控制器的
/// 地方，见 <see cref="AllModulesStartupModule"/>），为解析 TS 引一个 JS 运行时不划算。
/// 代价是必须贴着 api.ts 的既有写法，因此 <see cref="ScanResult.Unparsed"/> 把解析不出来的调用点
/// <b>逐条报出来</b>而不是静默丢掉 —— 一个只覆盖八成调用点的门禁，和没有门禁的区别只在于
/// 它会让人以为查过了。
/// </para>
/// </remarks>
internal static class FrontendApiScanner
{
    /// <summary>
    /// <c>client.&lt;method&gt;(</c> 调用点。
    /// </summary>
    /// <remarks>
    /// 泛型实参里可能有嵌套尖括号（<c>client.get&lt;PagedList&lt;AccountDto&gt;&gt;(</c>），
    /// 所以写成 <c>[^()]*</c> 而不是 <c>[^&gt;]*</c> —— 只要不跨过括号就行。
    /// 参数前允许换行：有 6 处调用把第一个参数写在下一行，<c>\s</c> 本身含换行故无需
    /// <c>Singleline</c>。
    /// </remarks>
    private static readonly Regex ClientCall = new(
        @"client\.([a-zA-Z]+)\s*(?:<[^()]*>)?\(\s*",
        RegexOptions.Compiled);

    /// <summary>
    /// 路径常量定义。
    /// </summary>
    /// <remarks>
    /// 两种作用域都要收：模块级 <c>const ADMIN_USER_BASE = '/admin/users';</c> 与
    /// <b>函数级</b> <c>const base = '/chat';</c>。后者在 <c>ai/api.ts</c> 里出现 12 次、
    /// 每次值不同（<c>/chat</c> / <c>/threads</c> / <c>/admin/agents</c> …），因此
    /// <see cref="ResolveConst"/> 必须按<b>位置</b>取最近的定义，绝不能建一张全局名字→值的表
    /// （那样 12 个函数会全部解析成最后一个值，且每一条都「成功解析」，不会有任何报错）。
    /// </remarks>
    private static readonly Regex ConstDef = new(
        @"const\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*'([^']*)'",
        RegexOptions.Compiled);

    /// <summary>
    /// 以 <c>client</c> 打头、后跟若干 <c>string</c> 形参的工厂函数定义。
    /// </summary>
    /// <remarks>
    /// <c>finance/api.ts</c> 用这个形状把 8 种单据（invoice / bill / expense / credit-memo /
    /// payment / transfer / estimate / purchase-order）的共同 CRUD 抽成两个泛型工厂
    /// （<c>documentApi</c> / <c>offerApi</c>），路径基址由**形参** <c>basePath</c> 传入。
    /// 不解析它，这一块约 60 个真实端点就全部脱离对账 —— 恰好是框架里体量最大的一块业务端点。
    /// </remarks>
    private static readonly Regex FactoryDef = new(
        @"function\s+(\w+)\s*(?:<[^()]*>)?\s*\(\s*client\s*:\s*HttpClient\s*((?:,\s*\w+\s*:\s*string\s*)+)\)\s*\{",
        RegexOptions.Compiled);

    /// <summary>工厂调用：<c>documentApi&lt;…&gt;(client, ADMIN_INVOICE_BASE)</c>。</summary>
    /// <remarks>泛型实参里有逗号但没有括号，故用 <c>[^()]*</c> 划界。</remarks>
    private static readonly Regex FactoryCall = new(
        @"\b(\w+)\s*(?:<[^()]*>)?\s*\(\s*client\s*,\s*([^()]*?)\)",
        RegexOptions.Compiled);

    private static readonly Regex ParamName = new(@",\s*(\w+)\s*:\s*string", RegexOptions.Compiled);

    /// <summary>
    /// 返回模板字符串的箭头函数常量：<c>const themeUrl = (scope: string) =&gt; `${BASE}/theme/${scope}`;</c>
    /// </summary>
    /// <remarks>
    /// 并入 <c>constDefs</c> 一起解析 —— 它和普通路径常量的区别只是调用时多一对括号，
    /// 模板里的 <c>${scope}</c> 照常成参数占位符。<c>system/api.ts</c> 的主题端点用了这个形状。
    /// </remarks>
    private static readonly Regex ArrowTemplateConst = new(
        @"const\s+(\w+)\s*=\s*\([^)]*\)\s*=>\s*`([^`]*)`",
        RegexOptions.Compiled);

    /// <summary>
    /// HttpClient 方法 → HTTP 动词。
    /// </summary>
    /// <remarks>
    /// <c>upload</c>/<c>uploadFormData</c> 内部是 <c>xhr.open('POST', …)</c>。
    /// <c>resolveUrl</c> 与 <c>download</c> 的动词从调用点读不出来，这里的值只是名义默认，
    /// 实际对账走 <see cref="RelaxedVerbs"/> 的动词集合。
    /// </remarks>
    private static readonly Dictionary<string, string> VerbMap = new(StringComparer.Ordinal)
    {
        ["get"] = "GET",
        ["post"] = "POST",
        ["put"] = "PUT",
        ["patch"] = "PATCH",
        ["delete"] = "DELETE",
        ["upload"] = "POST",
        ["uploadFormData"] = "POST",
        ["resolveUrl"] = "GET",
        ["download"] = "GET",
    };

    /// <summary>
    /// 动词无法从调用点静态确定的方法 → 该方法可接受的动词集合。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>download(url, { method: 'POST' })</c> —— 解析 options 对象字面量才能定死动词，
    /// 而那要求解析任意 TS 表达式；它只可能是 GET 或 POST（见 HttpClient 的签名）。
    /// </para>
    /// <para>
    /// <c>resolveUrl</c> 更彻底：它<b>不发请求</b>，只把路径拼成绝对 URL 交给别人用
    /// （SSE 流、img src、下载链接），动词由使用方决定，从调用点根本读不出来。实测三处
    /// 都指向 <c>[HttpPost]</c> 的流式端点（<c>/chat/stream</c>、
    /// <c>/admin/agents/{}/run/stream</c>、<c>/admin/workflows/{}/run/stream</c>），
    /// 按 GET 对账会把它们全报成「后端不存在」—— 一个纯粹由错误假设造出来的假阳性。
    /// 因此对它只校验<b>路径</b>存在于任意动词下。
    /// </para>
    /// <para>
    /// 代价是这两类方法上抓不到动词错配，保留的是主要目的：抓「这个端点根本不存在」。
    /// 放宽是显式的，不是遗漏。
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string[]> RelaxedVerbs = new(StringComparer.Ordinal)
    {
        ["download"] = ["GET", "POST"],
        ["resolveUrl"] = ["GET", "POST", "PUT", "PATCH", "DELETE"],
    };

    internal sealed record Call(string File, int Line, string Method, string Path)
    {
        /// <summary>该调用点可接受的 <c>"VERB /path"</c> 形态（通常一个，见 <see cref="RelaxedVerbs"/>）。</summary>
        public IEnumerable<string> AcceptableRoutes()
        {
            if (RelaxedVerbs.TryGetValue(Method, out var verbs))
                return verbs.Select(v => $"{v} {Path}");

            return [$"{VerbMap[Method]} {Path}"];
        }

        public override string ToString() => $"{File}:{Line} client.{Method} → {Path}";
    }

    internal sealed record ScanResult(
        IReadOnlyList<Call> Calls,
        IReadOnlyList<string> Unparsed,
        int TotalCallSites);

    /// <summary>扫 <c>packages/core/src/services/*/api.ts</c>。</summary>
    public static ScanResult Scan(string repoRoot)
    {
        var servicesDir = Path.Combine(
            repoRoot, "src", "Tnzi.UI", "packages", "core", "src", "services");

        var calls = new List<Call>();
        var unparsed = new List<string>();
        var total = 0;

        if (!Directory.Exists(servicesDir))
            return new ScanResult(calls, unparsed, total);

        foreach (var file in Directory.GetFiles(servicesDir, "api.ts", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            var text = File.ReadAllText(file);
            var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');

            var constDefs = ConstDef.Matches(text)
                .Select(m => (Pos: m.Index, Name: m.Groups[1].Value, Value: m.Groups[2].Value))
                .Concat(ArrowTemplateConst.Matches(text)
                    .Select(m => (Pos: m.Index, Name: m.Groups[1].Value, Value: m.Groups[2].Value)))
                .ToList();

            var factories = FindFactories(text, constDefs);

            foreach (Match call in ClientCall.Matches(text))
            {
                total++;
                var method = call.Groups[1].Value;
                var line = LineOf(text, call.Index);
                var where = $"{relative}:{line} client.{method}";

                if (!VerbMap.ContainsKey(method))
                {
                    // 不是发请求的方法（或新增了一个我们还不认识的）。前者无害，后者必须暴露 ——
                    // 静默跳过会让「新加的请求方法从此不受对账约束」这件事无人知晓。
                    unparsed.Add($"{where} — 未知的 HttpClient 方法，请在 VerbMap 里登记");
                    continue;
                }

                var argStart = call.Index + call.Length;

                // 落在工厂体内的调用点：形参有几组实参就产出几条路径（一个工厂方法服务 N 种单据）。
                var host = factories.FirstOrDefault(f => call.Index > f.BodyStart && call.Index < f.BodyEnd);
                var bindings = host?.Bindings ?? [EmptyBinding];

                var resolvedAny = false;
                foreach (var binding in bindings)
                {
                    var path = ExtractPath(text, argStart, constDefs, binding);
                    if (path == null)
                        continue;

                    resolvedAny = true;
                    calls.Add(new Call(relative, line, method, path));
                }

                if (!resolvedAny)
                    unparsed.Add($"{where} — 第一个参数不是模板字符串/字面量/已知常量/工厂形参");
            }
        }

        return new ScanResult(calls, unparsed, total);
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyBinding =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>一个工厂函数：体的范围 + 每个调用点带来的一组形参绑定。</summary>
    private sealed record Factory(
        int BodyStart,
        int BodyEnd,
        IReadOnlyList<IReadOnlyDictionary<string, string>> Bindings);

    /// <summary>找出所有工厂函数，并把它们各自的调用实参绑定到形参名上。</summary>
    private static List<Factory> FindFactories(
        string text,
        List<(int Pos, string Name, string Value)> constDefs)
    {
        var result = new List<Factory>();

        foreach (Match def in FactoryDef.Matches(text))
        {
            var name = def.Groups[1].Value;
            var paramNames = ParamName.Matches(def.Groups[2].Value)
                .Select(m => m.Groups[1].Value)
                .ToList();

            if (paramNames.Count == 0)
                continue;

            var braceIndex = def.Index + def.Length - 1;
            var bodyEnd = FindMatchingBrace(text, braceIndex);
            if (bodyEnd < 0)
                continue;

            var bindings = new List<IReadOnlyDictionary<string, string>>();
            foreach (Match callSite in FactoryCall.Matches(text))
            {
                if (!string.Equals(callSite.Groups[1].Value, name, StringComparison.Ordinal))
                    continue;
                if (callSite.Index > braceIndex && callSite.Index < bodyEnd)
                    continue; // 工厂体内的同名调用（递归/自引用），不是外部实参

                var args = callSite.Groups[2].Value
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                var binding = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var i = 0; i < paramNames.Count && i < args.Length; i++)
                {
                    var value = args[i].StartsWith('\'') && args[i].EndsWith('\'')
                        ? args[i].Trim('\'')
                        : ResolveConst(constDefs, args[i], callSite.Index);

                    if (value != null)
                        binding[paramNames[i]] = value;
                }

                if (binding.Count > 0)
                    bindings.Add(binding);
            }

            if (bindings.Count > 0)
                result.Add(new Factory(braceIndex, bodyEnd, bindings));
        }

        return result;
    }

    private static int FindMatchingBrace(string text, int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < text.Length; i++)
        {
            if (text[i] == '{')
                depth++;
            else if (text[i] == '}' && --depth == 0)
                return i;
        }

        return -1;
    }

    /// <summary>取调用点第一个参数并归一化成路由模板（<c>/files/{}</c>）。</summary>
    private static string? ExtractPath(
        string text,
        int start,
        List<(int Pos, string Name, string Value)> constDefs,
        IReadOnlyDictionary<string, string> binding)
    {
        if (start >= text.Length)
            return null;

        var raw = text[start] switch
        {
            '`' => ReadTemplate(text, start),
            '\'' => ReadDelimited(text, start, '\''),
            _ => ReadIdentifier(text, start) is { } id
                ? binding.GetValueOrDefault(id) ?? ResolveConst(constDefs, id, start)
                : FirstTemplateInArgs(text, start),
        };

        if (raw == null)
            return null;

        var expanded = ExpandInterpolations(
            raw,
            expr => binding.GetValueOrDefault(expr) ?? ResolveConst(constDefs, expr, start),
            out var unresolvedBase);

        return unresolvedBase || expanded == null ? null : Normalize(expanded);
    }

    /// <summary>
    /// 读一段反引号模板，正确跨过嵌套。
    /// </summary>
    /// <remarks>
    /// 必须跟踪 <c>${…}</c> 深度：<c>localization/api.ts</c> 里有
    /// <c>`${BASE}/missing${culture ? `?culture=${…}` : ''}`</c> —— 模板里嵌着模板。
    /// 只找「第一个反引号」会在内层开引号处截断，得到半截路径
    /// （<c>/admin/localization/missing${culture ?</c>），而它既不会报解析失败、
    /// 也永远匹配不上任何后端端点，于是变成一条自制的假孤儿。
    /// </remarks>
    private static string? ReadTemplate(string text, int start)
    {
        var sb = new StringBuilder();
        var depth = 0;

        for (var i = start + 1; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '`' && depth == 0)
                return sb.ToString();

            if (c == '$' && i + 1 < text.Length && text[i + 1] == '{')
            {
                depth++;
                sb.Append("${");
                i++;
                continue;
            }

            if (c == '}' && depth > 0)
                depth--;

            sb.Append(c);
        }

        return null;
    }

    /// <summary>
    /// 展开模板里的 <c>${…}</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 三条规则，每条都对应一个实际写法：
    /// </para>
    /// <para>
    /// ①能解析成常量/工厂形参 → 展开成字面值。
    /// </para>
    /// <para>
    /// ②解析不出、且插值<b>紧跟在 <c>/</c> 之后</b> → 路径参数，成 <c>{}</c>
    /// （<c>/files/${id}</c>）。
    /// </para>
    /// <para>
    /// ③解析不出、且<b>不</b>在 <c>/</c> 之后 → 它不是独立路径段，而是拼进上一段的东西，
    /// 实际只有查询串这一种情形（<c>?minutes=${m}</c>、<c>/missing${culture ? '?…' : ''}</c>）
    /// → 丢弃。后端路由模板里从来没有查询串，留着它必然报假孤儿。
    /// </para>
    /// <para>
    /// ★ 例外：<b>位置 0</b> 的插值是路径<b>基址</b>，解析不出时一律失败而不是猜。
    /// <c>${basePath}/${id}</c> 若把 basePath 也当占位符，会得到 <c>/{}/{}</c> ——
    /// 一条语法合法、语义错误的路由，可能碰巧撞上某个真实端点而静默通过。
    /// </para>
    /// </remarks>
    private static string? ExpandInterpolations(
        string raw,
        Func<string, string?> resolve,
        out bool unresolvedBase)
    {
        var sb = new StringBuilder();
        unresolvedBase = false;

        var i = 0;
        while (i < raw.Length)
        {
            if (raw[i] != '$' || i + 1 >= raw.Length || raw[i + 1] != '{')
            {
                sb.Append(raw[i++]);
                continue;
            }

            var close = FindMatchingBrace(raw, i + 1);
            if (close < 0)
                return null;

            var expr = raw[(i + 2)..close].Trim();
            var resolved = resolve(expr);

            if (resolved != null)
            {
                sb.Append(resolved);
            }
            else if (i == 0)
            {
                unresolvedBase = true;
                return null;
            }
            else if (sb.Length > 0 && sb[^1] == '/')
            {
                sb.Append("{}");
            }

            i = close + 1;
        }

        return sb.ToString();
    }

    private static string? ReadDelimited(string text, int start, char quote)
    {
        var end = text.IndexOf(quote, start + 1);
        return end < 0 ? null : text[(start + 1)..end];
    }

    private static string? ReadIdentifier(string text, int start)
    {
        var end = start;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
            end++;

        if (end == start)
            return null;

        // 必须紧跟 , 或 ) 或 ( —— 前两者是「常量当实参」，第三者是「箭头模板常量的调用」
        // （`themeUrl(scope)`）。其余（`foo.bar`、真正的函数调用）交给 ResolveConst 判空。
        var rest = text.AsSpan(end).TrimStart();
        if (rest.Length == 0 || (rest[0] != ',' && rest[0] != ')' && rest[0] != '('))
            return null;

        return text[start..end];
    }

    /// <summary>
    /// 兜底：在本调用的实参区里取第一个模板字符串。
    /// </summary>
    /// <remarks>
    /// 唯一的用途是<b>三元选路径</b>：<c>client.get(thresholdMs != null ? `…?a=1&amp;b=2` : `…?a=1`)</c>。
    /// 两个分支只差查询串，而查询串会被 <see cref="Normalize"/> 剥掉，所以取第一个分支即可。
    /// <para>
    /// 搜索范围严格截在<b>本次调用的实参区</b>内（按括号配对找到收尾的 <c>)</c>）。
    /// 第一版只截到「下一个 <c>client.</c> 之前」，那能跨出实参区 —— 两次调用之间的注释里
    /// 若有示例模板（本仓库的注释确实常写 <c>`${BASE}/foo`</c> 这种），就会被当成本次的路径。
    /// 猜错虽然不会静默通过（要么命中后端端点、要么报成孤儿），但报出来的位置会指向
    /// 一个与真正问题无关的路径，把人带到错误的地方。
    /// </para>
    /// </remarks>
    private static string? FirstTemplateInArgs(string text, int start)
    {
        var limit = FindArgsEnd(text, start);
        if (limit < 0)
            return null;

        var tick = text.IndexOf('`', start);
        return tick < 0 || tick >= limit ? null : ReadDelimited(text, tick, '`');
    }

    /// <summary>从实参区起点找到本次调用收尾的 <c>)</c>（<paramref name="start"/> 已在开括号之后）。</summary>
    private static int FindArgsEnd(string text, int start)
    {
        var depth = 1;
        for (var i = start; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '(':
                    depth++;
                    break;
                case ')' when --depth == 0:
                    return i;
            }
        }

        return -1;
    }

    /// <summary>取「位置在调用点之前、最靠近」的同名 const 值（函数级 <c>base</c> 靠这条正确）。</summary>
    private static string? ResolveConst(
        List<(int Pos, string Name, string Value)> constDefs,
        string name,
        int usePos)
    {
        (int Pos, string Name, string Value)? best = null;
        foreach (var d in constDefs)
        {
            if (d.Pos >= usePos || !string.Equals(d.Name, name, StringComparison.Ordinal))
                continue;
            if (best == null || d.Pos > best.Value.Pos)
                best = d;
        }

        return best?.Value;
    }

    /// <summary>
    /// 归一化：剥查询串、保证前导 <c>/</c>、去尾部 <c>/</c>、连续 <c>/</c> 折叠。
    /// </summary>
    /// <remarks>
    /// ★ 剥 <c>?</c> 及其后是必需的：4 个调用点把查询参数直接拼进路径
    /// （<c>`${ADMIN_PERFORMANCE_BASE}/endpoints?minutes=${m}&amp;topN=${n}`</c>），
    /// 而后端路由模板里从来没有查询串 —— 不剥就会把这几条报成「端点不存在」，
    /// 一个纯属自制的假阳性。
    /// </remarks>
    internal static string Normalize(string path)
    {
        var p = path.Trim();

        var query = p.IndexOf('?');
        if (query >= 0)
            p = p[..query];

        while (p.Contains("//", StringComparison.Ordinal))
            p = p.Replace("//", "/", StringComparison.Ordinal);

        if (!p.StartsWith('/'))
            p = "/" + p;
        if (p.Length > 1)
            p = p.TrimEnd('/');

        return p;
    }

    private static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
                line++;
        }

        return line;
    }
}
