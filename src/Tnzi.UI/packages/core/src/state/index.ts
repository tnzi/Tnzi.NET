/**
 * @tnzi/core/state
 *
 * 响应式状态管理逻辑层。
 * 基于 @vue/reactivity 实现，UI 包可直接使用或包装为 Pinia store。
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
