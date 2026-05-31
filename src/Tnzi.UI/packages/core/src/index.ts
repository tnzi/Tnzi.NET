/**
 * @tnzi/core
 *
 * Core utilities, types, and HTTP client for Tnzi.NET frontend applications.
 * Vue-reactive foundation for all Tnzi UI packages.
 * Fully aligned with Tnzi.NET backend framework.
 *
 * @packageDocumentation
 */

// Services (业务契约层)
export * from "./services/index";

// Types
export * from "./types/index";

// Shared UI DTOs (formerly components — Props/Emits moved to UI packages)
export * from "./types/shared-ui";

// HTTP
export * from "./http/index";

// Adapters (适配器接口)
export * from "./adapters/index";

// State (响应式状态管理逻辑层) ★  新增
export * from "./state/index";

// Headless (无头交互控制器) ★  新增
export * from "./headless/index";

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
