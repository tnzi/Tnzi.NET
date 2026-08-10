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
