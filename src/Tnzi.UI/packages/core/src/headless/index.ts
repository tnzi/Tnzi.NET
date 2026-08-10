/**
 * @tnzi/core/headless
 *
 * 无头交互逻辑层 - 基于 Vue 响应式系统的控制器，以及它们的组合式封装。
 * 不涉及 DOM。
 *
 * ★ `reactive` 一律从 `'vue'` 导入，**不要**改回 `'@vue/reactivity'`。两者导出
 * 的是同一个函数，但依赖追踪是**模块级状态**：消费方的 `computed()` 只认自己那
 * 份响应式运行时创建的代理。`vue` 是宿主必然自己持有并去重的那一份（每个
 * `@tnzi/*` 包都把它声明为 peer），而 `@vue/reactivity` 宿主没有理由直接依赖，
 * 于是在 `link:` 形态下会解析到框架自己的 node_modules —— 两份运行时，控制器写
 * 入的状态永远不会让消费方的 computed 失效。故障形态是**纯静默**的：列表卡在
 * 骨架屏、总数恒为 0、无报错无警告。见 `packages/ui/vite.mjs` 的单例说明。
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
