/**
 * @tnzi/core/state
 *
 * 响应式状态管理逻辑层。
 * 基于 Vue 响应式系统实现，UI 包可直接使用或包装为 Pinia store。
 *
 * ★ `reactive` 从 `'vue'` 导入而非 `'@vue/reactivity'`，理由见 `headless/index.ts`
 * 的同名说明：两份响应式运行时会让状态更新对消费方静默不可见。
 */

// 状态管理器
export { AuthStateManager, createInitialAuthState } from './auth';
export { UserStateManager, createInitialUserState } from './user';
export { AppStateManager, createInitialAppState } from './app';

// 运行时装配工厂 (HttpClient + AuthStateManager + authApi 一次接线)
export { createTnziClient } from './client';
export type { TnziClient, CreateTnziClientOptions } from './client';

// 所有类型
export * from './types/index';
