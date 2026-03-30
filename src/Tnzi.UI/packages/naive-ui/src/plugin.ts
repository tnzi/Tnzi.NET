/**
 * @tnzi/naive-ui/plugin
 *
 * Vue 3 plugin for Tnzi Naive UI components.
 *
 * Registers all T* components globally, configures message/dialog adapters,
 * and sets up theme/locale support for Naive UI.
 */

import type { App, Plugin } from 'vue';
import type { Locale as CoreLocale } from '@tnzi/core/adapters/i18n';
import { DEFAULT_LOCALE } from '@tnzi/core/adapters/i18n';
import { setMessageAdapter, setDialogAdapter } from '@tnzi/core/adapters';

// Import adapters
import { createNaiveMessageAdapter } from './adapters/message';
import { createNaiveDialogAdapter } from './adapters/dialog';

import { registerAllComponents } from './components/register';

// ============================================
// Plugin Options
// ============================================

export interface TnziNaiveUiOptions {
  /** @tnzi/core i18n locale (default: 'en') */
  locale?: CoreLocale;
  /** Whether to register all T* components globally (default: true) */
  registerComponents?: boolean;
  /**
   * Whether to register fallback adapters (default: true).
   *
   * When true, sets up message and dialog adapters using window.$message / window.$dialog
   * as the global handles. The application must wrap the root component with
   * NMessageProvider and NDialogProvider, and expose the APIs to the window object
   * (e.g., via a setup component that calls useMessage/useDialog).
   *
   * When false, adapters must be registered manually via setMessageAdapter/setDialogAdapter.
   */
  registerAdapters?: boolean;
}

// ============================================
// Plugin Factory
// ============================================

export function createTnziNaiveUi(options: TnziNaiveUiOptions = {}): Plugin {
  const {
    locale: _locale = DEFAULT_LOCALE,
    registerComponents = true,
    registerAdapters = true,
  } = options;

  return {
    install(app: App) {
      // 1. Register T* components globally
      if (registerComponents) {
        registerAllComponents(app);
      }

      // 2. Register adapters using window.$message / window.$dialog as global handles.
      //    Naive UI requires NMessageProvider / NDialogProvider in the component tree,
      //    and the actual API instances are typically exposed to window in a setup component.
      if (registerAdapters) {
        setMessageAdapter(createNaiveMessageAdapter());
        setDialogAdapter(createNaiveDialogAdapter());
      }

      // 3. Initialize i18n locale
      // TODO: Wire _locale to the i18n runtime. Currently, @tnzi/core i18n uses a
      // module-level singleton (createI18nContext) with setLocale(). The plugin should
      // call the shared i18n context's setLocale(_locale) here once a global i18n
      // context accessor is exposed from @tnzi/core (e.g., getI18nContext().setLocale).
      // For now, _locale is accepted but not applied — consumers must call setLocale()
      // manually on the core i18n context if a non-default locale is needed.
      void _locale;
    },
  };
}

/** Default plugin instance */
export default createTnziNaiveUi();
