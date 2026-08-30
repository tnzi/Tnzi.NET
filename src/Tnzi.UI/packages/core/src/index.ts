/**
 * @tnzi/core
 *
 * Core utilities, types, and HTTP client for Tnzi.NET frontend applications.
 * Vue-reactive foundation for all Tnzi UI packages.
 * Fully aligned with Tnzi.NET backend framework.
 *
 * @packageDocumentation
 */

// Services (业务契约层) —— 刻意**不**从根 barrel 再导出。
//
// 19 个后端模块的契约占本包源码的三分之二。把它们挂在根 barrel 上，会让
// `import { formatDateTime } from '@tnzi/core'` 这样一行付掉全部代价：实测
// 336,714 B 与 107 个 admin 端点常量，而调用方只想要一个日期格式化函数。
// 移出之后同一行是 21,154 B / 0 个端点。
//
// 服务一律按域从子路径取：
//
//   import { useAuthApi } from '@tnzi/core/services/identity'   // 3,568 B
//
// 每个域一个自包含 entry，摇树在那条路上本来就有效（坏的只有根 barrel）。
// 移除时零消费者：全部 `@tnzi/*` 包与已知消费应用里，没有任何一处写过
// `import { Identity } from '@tnzi/core'`。`@tnzi/ui-admin` 的 ~80 处根导入
// 全是 utils 里的格式化函数与 ApiResult / PagedList 这类基础类型。

// Types
export * from "./types/index";

// Shared UI DTOs (formerly components - Props/Emits moved to UI packages)
export * from "./types/shared-ui";

// HTTP
export * from "./http/index";

// Adapters (适配器接口)
export * from "./adapters/index";

// State (响应式状态管理逻辑层) ★  新增
export * from "./state/index";

// Headless (无头交互控制器) ★  新增
export * from "./headless/index";

// Guards (UI 框架无关的 vue-router 认证守卫工厂) ★  新增
export * from "./guards/index";

// Utilities
export * from "./utils/index";

// Constants
export * from "./constants/index";

// Errors
export * from "./errors/index";

// Re-export commonly used items for convenience
export { HttpError } from "./errors/api-error";
export { HttpClient, createHttpClient } from "./http/http";
export { getErrorMessage, isFailed, isSuccess } from "./http/response";
export type { ApiResult, PagedList, PagedQueryDto } from "./types/index";
