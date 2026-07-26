/**
 * @tnzi/core/headless
 *
 * 无头交互逻辑层 - 基于 @vue/reactivity 的响应式控制器。
 * 提供可组合的数据管理逻辑，不涉及 DOM。
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
export { FormController } from './form';
export type { FormFieldError, FormOptions, FormFieldState, FormState } from './form';

// 数据查询编排
export { DataQueryController } from './data-query';
export type { DataQueryStatus, DataQueryOptions } from './data-query';
