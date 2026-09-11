import { defineConfig } from "tsup";

export default defineConfig({
    entry: {
        // 主入口
        index: "src/index.ts",

        // 核心子路径
        "types/index": "src/types/index.ts",
        "types/shared-ui": "src/types/shared-ui.ts",
        "enums/index": "src/enums/index.ts",
        "http/index": "src/http/index.ts",
        "utils/index": "src/utils/index.ts",
        "constants/index": "src/constants/index.ts",
        "errors/index": "src/errors/index.ts",

        // 生成的图标清单：消费方要离线打包图标时读它，库自身不引用。
        "icons/index": "src/icons/index.ts",

        // 适配器
        "adapters/index": "src/adapters/index.ts",
        "adapters/i18n/index": "src/adapters/i18n/index.ts",
        "adapters/storage/index": "src/adapters/storage.ts",
        "adapters/theme/index": "src/adapters/theme/index.ts",
        "adapters/router/index": "src/adapters/router/index.ts",

        // 状态管理逻辑层 ( 新增)
        "state/index": "src/state/index.ts",

        // 无头交互控制器 ( 新增)
        "headless/index": "src/headless/index.ts",

        // 路由守卫 —— 只依赖调用方注入的认证原语，零运行时依赖。
        // 没有这条子路径时它只能从根 barrel 拿，而根 barrel 会把全部 19 个
        // service 的契约一起拖进消费方（2026-08-15 实测：@tnzi/ui-ai 为了
        // 一个 createTnziAuthGuard，把 107 个 admin 端点带进了纯对话应用）。
        "guards/index": "src/guards/index.ts",

        // 业务服务
        "services/ai/index": "src/services/ai/index.ts",
        "services/authorization/index": "src/services/authorization/index.ts",
        "services/identity/index": "src/services/identity/index.ts",
        "services/payment/index": "src/services/payment/index.ts",
        "services/finance/index": "src/services/finance/index.ts",
        "services/payroll/index": "src/services/payroll/index.ts",
        "services/chat/index": "src/services/chat/index.ts",
        "services/presence/index": "src/services/presence/index.ts",
        "services/notification/index": "src/services/notification/index.ts",
        "services/storage/index": "src/services/storage/index.ts",
        "services/system/index": "src/services/system/index.ts",
        "services/audit/index": "src/services/audit/index.ts",
        "services/template/index": "src/services/template/index.ts",
        "services/signing/index": "src/services/signing/index.ts",
        "services/logging/index": "src/services/logging/index.ts",
        "services/diagnostics/index": "src/services/diagnostics/index.ts",
        "services/performance/index": "src/services/performance/index.ts",
        "services/feature/index": "src/services/feature/index.ts",
        "services/signalr/index": "src/services/signalr/index.ts",
        "services/localization/index": "src/services/localization/index.ts",
    },
    format: ["cjs", "esm"],
    // tsup 8.5.1 hard-injects `baseUrl: "."` into the DTS program's compilerOptions
    // (rollup.js: `baseUrl: compilerOptions.baseUrl || "."`). Under TypeScript 6 a set
    // `baseUrl` raises TS5101 (deprecated, removed in TS 7) and aborts the DTS build.
    // tsup reads these options only from `dts.compilerOptions`, so silence it here.
    dts: {
        compilerOptions: {
            ignoreDeprecations: "6.0",
        },
    },
    // ★ 保持 `false`。2026-08-15 实测过开启，结论是**净损失**，别再"顺手优化"回去。
    //
    // 每个 entry 自包含时，消费方从子路径导入摇得很干净；开启 splitting 后
    // esbuild 按「被多个 entry 共享」切 chunk，一块 chunk 里混着多个 service 的
    // 代码，消费方只要碰到这块就得整块拿走。同一次改动三个消费应用的实测
    // （唯一 admin 端点常量数，越少越好）：
    //
    //                        chat     只走子路径的站点
    //   splitting:false        3            1
    //   splitting:true        41           37   <- 子路径消费方明显变差
    //
    // 根 barrel（`import { X } from '@tnzi/core'`）确实不可摇：拿两个符号要付
    // 336,714 B 与全部 107 个 admin 端点常量。但那条路的正解是**不走根 barrel** ——
    // 每个功能域都有子路径 entry，消费方按域导入即可（`guards` 的子路径就是这次
    // 补的，`__tests__/conventions/dist-treeshakeable.test.ts` 守着）。拿 splitting
    // 去救根 barrel，等于让所有按域导入的消费方替它买单。
    //
    // `package.json` 的 `"sideEffects": false` 同样试过：单加**一字节都不变**，
    // 只有与 splitting 同时开才对根 barrel 有效 —— 而那正是上面否掉的组合。
    //
    // （另见 2026-07-26 因「适配器单例被逐 entry 复制」否决 splitting 的记录：
    // 那个问题已由 `adapters/singleton.ts` 的 globalThis 注册表解决，与本条无关。）
    splitting: false,
    sourcemap: true,
    clean: true,
    treeshake: true,
    minify: false,
    // `vue` must stay external AND must be the only reactivity runtime this
    // package references. Bundling it - or importing `@vue/reactivity`
    // directly - gives the consumer a second reactivity instance whose proxies
    // no consumer `computed()` ever tracks. See `src/headless/index.ts`.
    external: ["vue"],
});
