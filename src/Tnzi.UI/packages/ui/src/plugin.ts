/**
 * @tnzi/ui/plugin
 *
 * Vue 3 plugin for Tnzi UI components (Naive UI).
 *
 * Registers all T* components globally, configures message/dialog adapters,
 * and sets up theme/locale support for Naive UI.
 */

import type { App, Plugin } from 'vue';
import type { Locale as CoreLocale } from '@tnzi/core/adapters/i18n';
import type { HttpClient } from '@tnzi/core/http/http';
import type { StorageAdapter } from '@tnzi/core/adapters';
import { DEFAULT_LOCALE, provideI18n } from '@tnzi/core/adapters/i18n';
import {
  setActiveUiAdapter,
  setActiveRuntimeAdapter,
  setNotificationAdapter,
  setLoadingBarAdapter,
} from '@tnzi/core/adapters';
import { provideStoreRuntime } from './stores/factory';

// Import adapters
import { createUiAdapter } from './adapters/create-ui-adapter';
import { createRuntimeAdapter, type VueRouterLike } from './adapters/create-runtime-adapter';
import { createNotificationAdapter } from './adapters/notification';
import { createLoadingBarAdapter } from './adapters/loading-bar';

import { registerAllComponents } from './components/register';

// Theme context (Phase 1.28)
import { defaultThemeSettings, mergeThemeSettings } from './theme/settings';
import { createThemeContext, THEME_CONTEXT_KEY } from './composables/theme/useTheme';
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
  /** HTTP client for store API calls (enables provide/inject DI, SSR-safe) */
  httpClient?: HttpClient;
  /** Storage adapter for persistence (used with httpClient for provide/inject DI) */
  storage?: StorageAdapter;
  /** Whether to register all T* components globally (default: true) */
  registerComponents?: boolean;
  /** Initial theme settings. Merged with defaults. */
  theme?: Partial<ThemeSettings>;
  /**
   * Whether to register fallback adapters (default: true).
   *
   * When true, sets up message, dialog, notification, and loading bar adapters
   * using window.$message / window.$dialog / window.$notification / window.$loadingBar
   * as the global handles. The application must wrap the root component with
   * NMessageProvider, NDialogProvider, NNotificationProvider, and NLoadingBarProvider,
   * and expose the APIs to the window object (e.g., via a setup component).
   *
   * When false, adapters must be registered manually.
   */
  registerAdapters?: boolean;
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
    registerAdapters = true,
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

      // 2. Register all adapters using window.$* global handles.
      //    Naive UI requires NMessageProvider / NDialogProvider / NNotificationProvider /
      //    NLoadingBarProvider in the component tree, and the actual API instances are
      //    typically exposed to window in a setup component.
      if (registerAdapters) {
        setActiveUiAdapter(createUiAdapter());
        setActiveRuntimeAdapter(createRuntimeAdapter({ router, storagePrefix }));
        setNotificationAdapter(createNotificationAdapter());
        setLoadingBarAdapter(createLoadingBarAdapter());
      }

      // 3. Provide store runtime via inject (SSR-safe)
      if (httpClient) {
        provideStoreRuntime(app, httpClient, storage);
      }

      // 4. Initialize i18n locale
      provideI18n(app, locale);
    },
  };
}

/** Default plugin instance */
export default createTnziUi();
