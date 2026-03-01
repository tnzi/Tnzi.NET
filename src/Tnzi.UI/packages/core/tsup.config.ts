import { defineConfig } from "tsup";

export default defineConfig({
    entry: {
        // 主入口
        index: "src/index.ts",

        // 核心子路径
        "types/index": "src/types/index.ts",
        "components/index": "src/components/index.ts",
        "enums/index": "src/enums/index.ts",
        "http/index": "src/http/index.ts",
        "utils/index": "src/utils/index.ts",
        "schemas/index": "src/schemas/index.ts",
        "constants/index": "src/constants/index.ts",
        "errors/index": "src/errors/index.ts",

        // 适配器
        "adapters/index": "src/adapters/index.ts",
        "adapters/i18n/index": "src/adapters/i18n/index.ts",
        "adapters/icons/index": "src/adapters/icons/index.ts",
        "adapters/storage/index": "src/adapters/storage.ts",
        "adapters/theme/index": "src/adapters/theme/index.ts",
        "adapters/router/index": "src/adapters/router/index.ts",

        // Store 类型定义
        "stores/index": "src/stores/index.ts",
        "stores/auth/index": "src/stores/auth/index.ts",
        "stores/user/index": "src/stores/user/index.ts",
        "stores/app/index": "src/stores/app/index.ts",

        // 状态管理逻辑层 ( 新增)
        "state/index": "src/state/index.ts",

        // 无头交互控制器 ( 新增)
        "headless/index": "src/headless/index.ts",

        // Composable 工厂函数
        "composables/index": "src/composables/index.ts",

        // Playground 共享基础设施
        "playground/index": "src/playground/index.ts",

        // 业务服务
        "services/ai/index": "src/services/ai/index.ts",
        "services/identity/index": "src/services/identity/index.ts",
        "services/payment/index": "src/services/payment/index.ts",
        "services/chat/index": "src/services/chat/index.ts",
        "services/notification/index": "src/services/notification/index.ts",
        "services/storage/index": "src/services/storage/index.ts",
        "services/system/index": "src/services/system/index.ts",
        "services/audit/index": "src/services/audit/index.ts",
        "services/template/index": "src/services/template/index.ts",
    },
    format: ["cjs", "esm"],
    dts: true,
    splitting: false,
    sourcemap: true,
    clean: true,
    treeshake: true,
    minify: false,
    external: ["@vue/reactivity"],
});
