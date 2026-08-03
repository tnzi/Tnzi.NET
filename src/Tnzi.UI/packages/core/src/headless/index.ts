/**
 * @tnzi/core/headless
 *
 * 无头交互逻辑层 - 基于 @vue/reactivity 的响应式控制器，以及它们的
 * 组合式封装。不涉及 DOM。
 *
 * 全生态统一：**无渲染逻辑一律住在 `headless/`**（core / ui / ui-admin /
 * ui-ai / mobile 五个包同名），不再区分「控制器类」与「composable 工厂」——
 * 那个区分曾经让同一层的东西散在 `headless/` 和 `composables/` 两处，而判据
 * （是不是 class）对消费方毫无意义：他们要回答的只是「这段逻辑在哪」。
 */

// 分页
export { PaginationController, initialPaginationState, normalizePageSize, calculateTotalPages, clampPageIndex, updatePageQuery } from './pagination';
export type { PaginationOptions, PaginationState } from './pagination';

// 选择
export { SelectionController } from './selection';
export type { SelectionMode, SelectionOptions } from './selection';

// 排序
export { SortController, toggleSort } from './sort';
export type { SortDirection, SortField, SortOptions, HeadlessSortDirection, SortState } from './sort';

// 表单

// 数据查询编排
export { DataQueryController } from './data-query';
export type { DataQueryStatus, DataQueryOptions } from './data-query';

// 数据查询的组合式封装（生命周期 + 自动刷新）
export { useDataQuery } from './useDataQuery';
export type { UseDataQueryOptions, UseDataQueryReturn } from './useDataQuery';
