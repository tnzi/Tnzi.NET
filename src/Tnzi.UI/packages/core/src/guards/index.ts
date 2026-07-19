/**
 * @tnzi/core/guards
 *
 * 轻量、UI 框架无关的 vue-router 认证守卫工厂。
 *
 * 与 `@tnzi/ui-admin` 的 `createAuthGuard` 不同,本工厂不依赖任何 UI 包、
 * 不依赖 admin store / 菜单,只吃调用方注入的认证原语(是否已登录、恢复会话),
 * 因此可被 mobile / ui-ai / admin 等任意消费方复用,尤其是没有 admin 框架的
 * 轻量应用(如用 @tnzi/ui-ai 的纯 chat 应用)不必再手写 `router.beforeEach`。
 *
 * 设计:纯 TS,零运行时依赖。类型是 vue-router `RouteLocationNormalized` /
 * `RouteLocationRaw` 的结构子集,故返回的守卫函数可直接传给 `router.beforeEach`
 * 而无需在 core 里引入 vue-router 依赖。守卫用"返回重定向目标"的现代写法
 * (返回 true/undefined 放行、返回目标则导航),不使用 `next` 回调。
 *
 * @example
 * ```ts
 * import { createRouter } from 'vue-router'
 * import { createTnziAuthGuard } from '@tnzi/core'
 * import { auth } from './app' // 内部持有一个 AuthStateManager
 *
 * const router = createRouter({ ... })
 *
 * router.beforeEach(createTnziAuthGuard({
 *   isLoggedIn: () => auth.isLoggedIn,          // 读当前认证态
 *   restore: () => auth.restoreAuth(),          // 首次导航前恢复持久化会话
 *   loginRouteName: 'login',                    // 登录路由名(默认 'login')
 *   publicRouteNames: ['forgot-password'],      // 无需登录即可访问的路由
 *   homeRouteName: 'home',                       // 已登录再访问登录页时回首页(可省)
 * }))
 * ```
 */

/**
 * 守卫读取的最小路由结构。
 *
 * 是 vue-router `RouteLocationNormalized` 的结构子集:更"宽"的实际路由对象
 * 可被赋给它,故 `router.beforeEach` 调用时传入的富路由对象与本类型结构兼容。
 */
export interface TnziGuardRoute {
  /** 解析后的路径,如 `/admin/users`。 */
  path: string;
  /** 命名路由的名称(未命名路由为 null / undefined)。 */
  name?: string | symbol | null;
  /** 含 query 与 hash 的完整路径,用于登录后回跳。 */
  fullPath: string;
  /** query 参数包。 */
  query: Record<string, unknown>;
  /** 路由 meta 元数据(读 `meta.requiresAuth === false` 作为逐路由豁免)。 */
  meta: Record<string, unknown>;
}

/**
 * 守卫可返回的重定向目标。
 *
 * 是 vue-router `RouteLocationRaw` 的结构子集:字符串路径,或命名 / 路径定位对象。
 */
export type TnziGuardRedirect =
  | string
  | { name: string; query?: Record<string, string | number | null | undefined>; replace?: boolean }
  | { path: string; query?: Record<string, string | number | null | undefined>; replace?: boolean };

/**
 * 守卫返回值:
 * - `true` / `undefined`:放行;
 * - `false`:中止导航;
 * - 重定向目标:导航到该目标。
 */
export type TnziGuardResult = boolean | void | TnziGuardRedirect;

/** 返回的守卫函数类型(与 vue-router `NavigationGuard` 结构兼容)。 */
export type TnziAuthGuard = (to: TnziGuardRoute, from: TnziGuardRoute) => Promise<TnziGuardResult>;

/** 传给 {@link createTnziAuthGuard} 的配置。 */
export interface TnziAuthGuardOptions {
  /**
   * 读取当前是否已登录(必填)。通常是 `() => authManager.isLoggedIn`。
   * 每次受守卫的导航都会调用,应是廉价的同步读取。
   */
  isLoggedIn: () => boolean;
  /**
   * 可选:在首次受守卫的导航前恢复会话(如冷刷新后从持久化 token 重建认证态)。
   * 仅执行一次(内存化,并发去重);可返回布尔表示恢复后是否已登录,
   * 但最终判定仍以 `isLoggedIn()` 为准,返回值仅供调用方自用。
   */
  restore?: () => Promise<boolean | void>;
  /** 登录路由名,默认 `'login'`。登录路由本身始终视为公共可达。 */
  loginRouteName?: string;
  /** 无需登录即可访问的路由名列表(按 `route.name` 匹配)。 */
  publicRouteNames?: string[];
  /**
   * 可选:已登录用户再访问登录页时重定向到的首页路由名。
   * 不设则放行(允许已登录用户停留在登录页)。
   */
  homeRouteName?: string;
  /**
   * 未登录被拦截时,登录目标上携带回跳地址的 query 键,默认 `'redirect'`。
   * 例如生成 `{ name: 'login', query: { redirect: '/admin/users' } }`。
   */
  redirectQueryKey?: string;
  /**
   * 可选:自定义最终重定向目标。收到默认目标与原因,可返回:
   * 新目标 / `true`(改为放行) / `false`(中止) / `undefined`(沿用默认目标)。
   */
  resolveRedirect?: (ctx: {
    to: TnziGuardRoute;
    reason: 'unauthenticated' | 'already-authenticated';
    defaultTarget: TnziGuardRedirect;
  }) => TnziGuardResult;
}

/**
 * 创建一个 vue-router `beforeEach` 认证守卫。
 *
 * 语义:
 * 1. 首次受守卫的导航前调用 `restore()` 一次(恢复持久化会话)。
 * 2. 已登录:访问登录页且配置了 `homeRouteName` 则重定向首页,否则放行。
 * 3. 未登录:目标是登录页 / 公共路由 / `meta.requiresAuth === false` 则放行;
 *    其余重定向到登录路由并携带 `?<redirectQueryKey>=<目标 fullPath>` 以便登录后回跳。
 *
 * @see {@link TnziAuthGuardOptions}
 */
export function createTnziAuthGuard(options: TnziAuthGuardOptions): TnziAuthGuard {
  const {
    isLoggedIn,
    restore,
    loginRouteName = 'login',
    publicRouteNames = [],
    homeRouteName,
    redirectQueryKey = 'redirect',
    resolveRedirect,
  } = options;

  const publicNames = new Set<string>(publicRouteNames);

  // restore 只在首次受守卫的导航时执行一次(内存化 + 并发去重)。
  let restored = false;
  let restorePromise: Promise<boolean | void> | null = null;

  const runRestore = async (): Promise<void> => {
    if (restored || !restore) return;
    if (!restorePromise) restorePromise = restore();
    try {
      await restorePromise;
    } finally {
      restored = true;
      restorePromise = null;
    }
  };

  const routeName = (route: TnziGuardRoute): string =>
    typeof route.name === 'string' ? route.name : '';

  const isPublicRoute = (route: TnziGuardRoute): boolean => {
    if (route.meta?.requiresAuth === false) return true;
    const name = routeName(route);
    if (name && name === loginRouteName) return true;
    return name.length > 0 && publicNames.has(name);
  };

  const finalize = (
    reason: 'unauthenticated' | 'already-authenticated',
    to: TnziGuardRoute,
    defaultTarget: TnziGuardRedirect,
  ): TnziGuardResult => {
    if (!resolveRedirect) return defaultTarget;
    const custom = resolveRedirect({ to, reason, defaultTarget });
    return custom === undefined ? defaultTarget : custom;
  };

  return async (to, _from) => {
    // 首次导航先尝试恢复会话(冷刷新后重建 token),再判定登录态。
    await runRestore();

    if (isLoggedIn()) {
      // 已登录访问登录页:配置了首页则回首页,否则放行。
      if (homeRouteName && routeName(to) === loginRouteName) {
        return finalize('already-authenticated', to, { name: homeRouteName });
      }
      return true;
    }

    // 未登录:登录页 / 公共路由放行,其余重定向登录并携带回跳地址。
    if (isPublicRoute(to)) return true;

    return finalize('unauthenticated', to, {
      name: loginRouteName,
      query: { [redirectQueryKey]: to.fullPath },
    });
  };
}
