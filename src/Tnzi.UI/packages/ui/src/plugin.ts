/**
 * @tnzi/ui/plugin
 *
 * Vue 3 plugin for the library-neutral Tnzi UI layer: the theme context, the
 * runtime adapter, i18n, the Pinia store runtime and global registration of
 * this package's T* components.
 *
 * It registers no message / dialog / notification / loading-bar adapter:
 * those drive a component library and are registered by the package that
 * renders with it (`createTnziUiAdmin` in `@tnzi/ui-admin`).
 *
 * i18n note: the message catalogue is `@tnzi/core/adapters/i18n` (namespaces
 * `common` / `auth` / `crud` / `table` / `form` / `upload` / `date`), provided
 * here via `provideI18n`. This package does NOT ship a `tnzi.ui.*` bundle of
 * its own - one existed, was read by nothing, and was removed on 2026-08-01.
 */

import type { App, Plugin } from 'vue';
import type { Locale as CoreLocale } from '@tnzi/core/adapters/i18n';
import type { HttpClient } from '@tnzi/core/http';
import type { StorageAdapter } from '@tnzi/core/adapters';
import { DEFAULT_LOCALE, provideI18n } from '@tnzi/core/adapters/i18n';
import { setActiveRuntimeAdapter } from '@tnzi/core/adapters';
import { initStoreRuntime } from './stores/factory';
import { createRuntimeAdapter, type VueRouterLike } from './adapters/create-runtime-adapter';
import { registerAllComponents } from './components/register';
import { defaultThemeSettings, mergeThemeSettings } from './theme/settings';
import { createThemeContext, THEME_CONTEXT_KEY } from './headless/theme/useTheme';
import type { ThemeSettings } from './theme/types';

// ============================================
// Plugin Options
// ============================================

export interface TnziUiOptions {
  /** @tnzi/core i18n locale (default: 'en') */
  locale?: CoreLocale;
  /** vue-router instance for runtime adapter navigation */
  router?: VueRouterLike;
  /** localStorage key prefix for runtime adapter storage */
  storagePrefix?: string;
  /**
   * HTTP client for the Pinia stores in `@tnzi/ui/stores`. Sets the module-level
   * store runtime (`initStoreRuntime`), which every app in the page shares.
   */
  httpClient?: HttpClient;
  /** Storage adapter for those stores' persistence (used with `httpClient`). */
  storage?: StorageAdapter;
  /** Whether to register all T* components globally (default: true) */
  registerComponents?: boolean;
  /** Initial theme settings. Merged with defaults. */
  theme?: Partial<ThemeSettings>;
}

// ============================================
// Plugin Factory
// ============================================

export function createTnziUi(options: TnziUiOptions = {}): Plugin {
  const {
    locale = DEFAULT_LOCALE,
    router,
    storagePrefix,
    httpClient,
    storage,
    theme,
    registerComponents = true,
  } = options;

  return {
    install(app: App) {
      // 0. Provide theme context (Phase 1.28)
      const initialThemeSettings = theme
        ? mergeThemeSettings(theme)
        : { ...defaultThemeSettings };
      const themeContext = createThemeContext(initialThemeSettings);
      app.provide(THEME_CONTEXT_KEY, themeContext);

      // 1. Register T* components globally
      if (registerComponents) {
        registerAllComponents(app);
      }

      // 2. The runtime adapter (navigation + storage) that @tnzi/core uses
      setActiveRuntimeAdapter(createRuntimeAdapter({ router, storagePrefix }));

      // 3. Point the Pinia stores at the given HTTP client (module-level runtime)
      if (httpClient) {
        initStoreRuntime(httpClient, storage);
      }

      // 4. Initialize i18n locale
      provideI18n(app, locale);
    },
  };
}

/** Default plugin instance */
export default createTnziUi();
