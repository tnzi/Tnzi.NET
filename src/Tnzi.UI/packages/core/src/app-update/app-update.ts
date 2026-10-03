/**
 * 发版后旧页面的自愈：分块加载失败恢复 + 新版本检测。
 *
 * ## 要解决的问题
 *
 * SPA 上线后用户一直开着标签页，期间发了新版本。旧页面手里的是旧 `index.html`
 * 引用的旧分块名；部署把旧 `assets/` 换掉之后，懒加载路由的 `import()` 拿到 404，
 * 表现是「点菜单没反应」「某一页白屏」，控制台一句 `Failed to fetch dynamically
 * imported module`。没删旧文件的部署也有问题：用户一直跑在旧版本上，直到自己手动刷新。
 *
 * ## 两件事，各自独立
 *
 * 1. **分块加载失败恢复**：路由懒加载失败时整页跳到用户本来要去的地址，拿到新外壳。
 *    `vite:preloadError`（路由以外的懒组件）同样处理，跳回当前地址。
 * 2. **新版本检测**：以 `no-store` 重新取一次外壳 HTML，比较入口模块脚本的地址。
 *    带 hash 的入口文件名本身就是版本号，所以**不需要任何构建插件或 `version.json`**。
 *    发现新版本后默认在**下一次路由跳转时**整页加载目标地址，不会冲掉正在填的表单。
 *
 * ## 防循环
 *
 * 两条路径都会整页加载，所以都必须防止「加载后还是旧的 → 再加载」的死循环：
 *
 * - 分块恢复：同一个目标地址在 {@link RECOVERY_WINDOW_MS} 内只重试一次。第二次失败
 *   说明不是发版造成的（真的缺文件、网络断了），交还给调用方的错误处理。
 * - 版本刷新：记下「为哪个远端指纹刷新过」。刷新后指纹仍然对不上（外壳被 CDN 或
 *   代理缓存住了），同一个远端指纹不会再触发第二次刷新。
 *
 * 两份记录都放 `sessionStorage`。读写失败（隐私模式、被禁用）时**不自动刷新**：
 * 没有防循环记录的自动刷新可能无限循环，而不刷新最坏只是退回到今天的行为。
 *
 * ## 开发模式
 *
 * 文档里出现 `/@vite/client` 即视为 Vite 开发服务器，整个机制不启用：开发时的
 * 依赖重新预构建也会报同样的动态导入错误，而 Vite 自己会处理刷新。
 */
import { createAdapterSingleton } from '../adapters/singleton';

/** 发现新版本后怎么办。 */
export type AppUpdateMode =
  /** 下一次路由跳转时整页加载目标地址（默认）。没有路由时退化为只通知。 */
  | 'navigate'
  /** 调用 `prompt` 询问用户；拒绝后同一个版本不再询问。没有 `prompt` 时按 `navigate` 处理。 */
  | 'prompt'
  /** 只检测、只通知（`onUpdateAvailable`），不自动刷新。 */
  | 'notify';

/** 守卫与错误处理读取的最小路由结构，是 vue-router `RouteLocationNormalized` 的结构子集。 */
export interface AppUpdateRoute {
  fullPath: string;
}

/**
 * 用到的最小路由器结构，是 vue-router `Router` 的结构子集，core 因此不必依赖 vue-router。
 */
export interface AppUpdateRouter {
  beforeEach(guard: (to: AppUpdateRoute, from: AppUpdateRoute) => boolean | void): () => void;
  onError(handler: (error: unknown, to: AppUpdateRoute, from: AppUpdateRoute) => void): () => void;
  resolve(to: string): { href: string };
}

export interface AppUpdateOptions {
  /** 发现新版本后的处理方式。默认 `'navigate'`。 */
  mode?: AppUpdateMode;
  /**
   * 应用的路由器。提供时：懒加载路由失败会跳到本来要去的地址；`navigate` 模式在
   * 下一次跳转时刷新。不提供时只处理 `vite:preloadError`，版本更新只通知。
   */
  router?: AppUpdateRouter;
  /**
   * 去哪里取最新的外壳 HTML。默认取当前地址（去掉 hash）：SPA 的每个深链本来就要
   * 回落到 `index.html`，否则刷新页面本身就会 404。
   */
  shellUrl?: string | (() => string);
  /** 定时检查的间隔（毫秒）。默认 5 分钟；`0` 关闭定时检查，只在标签页回到前台时检查。 */
  checkInterval?: number;
  /** `'prompt'` 模式下询问用户是否立即刷新，返回 `true` 立即刷新。 */
  prompt?: () => Promise<boolean>;
  /** 每发现一个新版本调用一次。可用于自定义提示。 */
  onUpdateAvailable?: () => void;
  /** 是否启用分块加载失败恢复。默认 `true`。 */
  recoverChunkErrors?: boolean;
}

export interface AppUpdateHandle {
  /** 立即检查一次。返回是否已有可用的新版本。 */
  checkNow(): Promise<boolean>;
  /** 是否已检测到新版本。 */
  readonly updateAvailable: boolean;
  /** 移除全部监听器与定时器。 */
  dispose(): void;
}

/** 默认检查间隔：5 分钟。 */
export const DEFAULT_APP_UPDATE_INTERVAL_MS = 5 * 60 * 1000;

/** 两次检查之间的最小间隔，避免反复切换标签页时连发请求。 */
const MIN_CHECK_GAP_MS = 30 * 1000;

/** 同一个目标地址的分块恢复在这个时间窗内只重试一次。 */
export const RECOVERY_WINDOW_MS = 10 * 1000;

const RECOVERY_KEY = 'tnzi:app-update:recovery';
const RELOADED_FOR_KEY = 'tnzi:app-update:reloaded-for';
const DEV_CLIENT_PATH = '/@vite/client';

/**
 * 各浏览器与 Vite 对「分块加载失败」的报法：
 * Chrome / Edge、Firefox、Safari 的动态导入失败，Vite 的 CSS 预加载失败，
 * 以及服务器把缺失的脚本回落成 HTML 时的 MIME 类型错误。
 */
const CHUNK_ERROR_PATTERNS: readonly RegExp[] = [
  /Failed to fetch dynamically imported module/i,
  /error loading dynamically imported module/i,
  /Importing a module script failed/i,
  /Unable to preload CSS/i,
  /is not a valid JavaScript MIME type/i,
];

/** 判断一个错误是不是「懒加载的分块拿不到」。 */
export function isChunkLoadError(error: unknown): boolean {
  if (!error) return false;
  const name = (error as { name?: unknown }).name;
  if (name === 'ChunkLoadError') return true;
  const message =
    typeof error === 'string' ? error : String((error as { message?: unknown }).message ?? '');
  return CHUNK_ERROR_PATTERNS.some((pattern) => pattern.test(message));
}

/**
 * 从一份 HTML 文档里取版本指纹：全部入口模块脚本的路径，排序后拼接。
 * 没有入口脚本（维护页、网关错误页）时返回空串，调用方据此忽略这次结果。
 */
export function readShellFingerprint(doc: Document, baseUrl: string): string {
  const sources = Array.from(doc.querySelectorAll('script[type="module"][src]'))
    .map((script) => script.getAttribute('src') ?? '')
    .filter((src) => src !== '')
    .map((src) => {
      try {
        return new URL(src, baseUrl).pathname;
      } catch {
        return src;
      }
    });
  return [...new Set(sources)].sort().join('|');
}

function isDevServer(doc: Document): boolean {
  return Array.from(doc.querySelectorAll('script[src]')).some((script) =>
    (script.getAttribute('src') ?? '').includes(DEV_CLIENT_PATH),
  );
}

function readSession(key: string): string | null | undefined {
  try {
    return window.sessionStorage.getItem(key);
  } catch {
    return undefined;
  }
}

function writeSession(key: string, value: string): boolean {
  try {
    window.sessionStorage.setItem(key, value);
    return true;
  } catch {
    return false;
  }
}

/**
 * 记录一次分块恢复并判断是否允许。同一目标在时间窗内已经试过一次，或者
 * 存储不可用（无法防循环），都返回 `false`。
 */
function claimRecovery(href: string): boolean {
  const raw = readSession(RECOVERY_KEY);
  if (raw === undefined) return false;
  if (raw) {
    try {
      const last = JSON.parse(raw) as { href?: string; at?: number };
      if (last.href === href && typeof last.at === 'number' && Date.now() - last.at < RECOVERY_WINDOW_MS) {
        return false;
      }
    } catch {
      // 记录损坏，按没有记录处理。
    }
  }
  return writeSession(RECOVERY_KEY, JSON.stringify({ href, at: Date.now() }));
}

/** 记录「为这个远端指纹刷新过」。已经刷新过或存储不可用时返回 `false`。 */
function claimReloadFor(fingerprint: string): boolean {
  const last = readSession(RELOADED_FOR_KEY);
  if (last === undefined || last === fingerprint) return false;
  return writeSession(RELOADED_FOR_KEY, fingerprint);
}

/** 路由器给出的是相对地址，当前地址是绝对地址；防循环记录按绝对地址比较。 */
function absolute(href: string): string {
  return new URL(href, window.location.href).href;
}

function loadPage(href: string): void {
  if (href === window.location.href) {
    window.location.reload();
  } else {
    window.location.assign(href);
  }
}

function currentHref(): string {
  return window.location.href;
}

function defaultShellUrl(): string {
  const url = new URL(window.location.href);
  url.hash = '';
  return url.href;
}

const activeHandle = createAdapterSingleton<AppUpdateHandle | null>('app-update', () => null);

const inertHandle: AppUpdateHandle = {
  checkNow: async () => false,
  updateAvailable: false,
  dispose: () => undefined,
};

/**
 * 安装发版自愈机制。每个页面只生效一次：重复调用返回已安装的那一份，
 * 所以应用自己装过之后，框架的启动入口再装也不会叠加监听器。
 *
 * 服务端须保证外壳 HTML 不被缓存（`Cache-Control: no-cache`），否则刷新后拿到的
 * 仍是缓存里的旧外壳。框架托管前端时已经这样返回；自行部署静态文件时需要自己配置。
 */
export function installAppUpdate(options: AppUpdateOptions = {}): AppUpdateHandle {
  if (typeof window === 'undefined' || typeof document === 'undefined') return inertHandle;

  const existing = activeHandle.peek();
  if (existing) return existing;

  if (isDevServer(document)) return inertHandle;

  const mode: AppUpdateMode = options.mode ?? 'navigate';
  const router = options.router;
  const interval = options.checkInterval ?? DEFAULT_APP_UPDATE_INTERVAL_MS;
  const resolveShellUrl = (): string =>
    typeof options.shellUrl === 'function'
      ? options.shellUrl()
      : (options.shellUrl ?? defaultShellUrl());

  const baseline = readShellFingerprint(document, window.location.href);
  const cleanups: Array<() => void> = [];

  // 一次加载只发起一次整页跳转：路由错误与 vite:preloadError 会为同一次失败各报一次。
  let leaving = false;
  let remoteFingerprint = '';
  let promptedFor = '';
  let lastCheckAt = 0;
  let inFlight: Promise<boolean> | null = null;

  const leaveTo = (href: string): void => {
    leaving = true;
    loadPage(href);
  };

  const recover = (target: string): boolean => {
    if (leaving) return true;
    const href = absolute(target);
    if (!claimRecovery(href)) return false;
    leaveTo(href);
    return true;
  };

  if (options.recoverChunkErrors !== false) {
    // 同一次路由懒加载失败会被报两次：先是 vite:preloadError，再是路由的 onError。
    // 路由那一方认领之后，即使它被防循环记录拒绝，preloadError 也不得改为重载当前地址：
    // 那是错误的目标，而且会绕过刚刚生效的防循环。
    let routeClaimed = false;

    if (router) {
      cleanups.push(
        router.onError((error, to) => {
          if (!isChunkLoadError(error)) return;
          routeClaimed = true;
          // 在 preloadError 的宏任务之后复位，只覆盖这一次失败。
          window.setTimeout(() => {
            routeClaimed = false;
          }, 0);
          recover(router.resolve(to.fullPath).href);
        }),
      );
    }

    // 路由以外的懒组件。推迟到宏任务：同一次失败若来自路由懒加载，路由的 onError
    // 会在 promise 拒绝链上先拿到它，这里就交给它处理。
    const onPreloadError = (): void => {
      window.setTimeout(() => {
        if (!routeClaimed) recover(currentHref());
      }, 0);
    };
    window.addEventListener('vite:preloadError', onPreloadError);
    cleanups.push(() => window.removeEventListener('vite:preloadError', onPreloadError));
  }

  const reloadForUpdate = (target: string): boolean => {
    if (leaving) return true;
    if (!claimReloadFor(remoteFingerprint)) return false;
    leaveTo(absolute(target));
    return true;
  };

  const onDetected = (): void => {
    options.onUpdateAvailable?.();
    if (mode !== 'prompt' || !options.prompt || promptedFor === remoteFingerprint) return;
    promptedFor = remoteFingerprint;
    void options.prompt().then(
      (accepted) => {
        if (accepted) reloadForUpdate(currentHref());
      },
      () => undefined,
    );
  };

  const check = async (): Promise<boolean> => {
    if (!baseline) return false;
    let response: Response;
    try {
      response = await fetch(resolveShellUrl(), {
        cache: 'no-store',
        credentials: 'same-origin',
        headers: { Accept: 'text/html' },
      });
    } catch {
      return remoteFingerprint !== '';
    }
    if (!response.ok || !(response.headers.get('content-type') ?? '').includes('text/html')) {
      return remoteFingerprint !== '';
    }
    let html: string;
    try {
      html = await response.text();
    } catch {
      return remoteFingerprint !== '';
    }
    const remote = readShellFingerprint(new DOMParser().parseFromString(html, 'text/html'), response.url || resolveShellUrl());
    if (!remote || remote === baseline) return remoteFingerprint !== '';
    if (remote !== remoteFingerprint) {
      remoteFingerprint = remote;
      onDetected();
    }
    return true;
  };

  const checkThrottled = (): void => {
    if (inFlight || Date.now() - lastCheckAt < MIN_CHECK_GAP_MS) return;
    lastCheckAt = Date.now();
    inFlight = check().finally(() => {
      inFlight = null;
    });
  };

  if (router && mode !== 'notify') {
    cleanups.push(
      router.beforeEach((to, from) => {
        if (!remoteFingerprint || to.fullPath === from.fullPath) return;
        // prompt 模式且用户已经回答过：不再在跳转时自动刷新，尊重「稍后」。
        if (mode === 'prompt' && options.prompt) return;
        return reloadForUpdate(router.resolve(to.fullPath).href) ? false : undefined;
      }),
    );
  }

  const onVisibility = (): void => {
    if (document.visibilityState === 'visible') checkThrottled();
  };
  document.addEventListener('visibilitychange', onVisibility);
  cleanups.push(() => document.removeEventListener('visibilitychange', onVisibility));

  if (interval > 0) {
    const timer = window.setInterval(() => {
      if (document.visibilityState === 'visible') checkThrottled();
    }, interval);
    cleanups.push(() => window.clearInterval(timer));
  }

  const handle: AppUpdateHandle = {
    checkNow: async () => {
      lastCheckAt = Date.now();
      return check();
    },
    get updateAvailable() {
      return remoteFingerprint !== '';
    },
    dispose: () => {
      for (const cleanup of cleanups.splice(0)) cleanup();
      if (activeHandle.peek() === handle) activeHandle.reset();
    },
  };
  activeHandle.set(handle);
  return handle;
}
