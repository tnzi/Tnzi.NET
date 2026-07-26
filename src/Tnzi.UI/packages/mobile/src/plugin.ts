/**
 * @tnzi/mobile/plugin
 *
 * Vue 3 plugin for Vant-based mobile SPA applications.
 */

import type { App, Plugin } from 'vue';
import { Locale } from 'vant';
import zhCN from 'vant/es/locale/lang/zh-CN';
import enUS from 'vant/es/locale/lang/en-US';
import { provideI18n } from '@tnzi/core/adapters/i18n';
import { setActiveUiAdapter, setActiveRuntimeAdapter } from '@tnzi/core/adapters';
import { registerAllComponents } from './components/register';
import { createVantUiAdapter } from './adapters/create-ui-adapter';
import { createVantRuntimeAdapter } from './adapters/create-runtime-adapter';
import type { VantRuntimeAdapterOptions } from './adapters/create-runtime-adapter';
import './styles/vant.css';
import './styles/dialog.css';
// Atomic utilities used by this package's components. Emitted into the same
// `dist/style.css` consumers already import.
import 'virtual:uno.css';

/** Locale shared by Vant's own strings and @tnzi/core's `t()`. */
export type TnziMobileLocale = 'zh-CN' | 'en-US';

/** Plugin options */
export interface TnziMobileOptions {
  /** Locale for Vant strings and @tnzi/core i18n (default: 'en-US') */
  locale?: TnziMobileLocale;
  /** Whether to register components globally (default: true) */
  registerComponents?: boolean;
  /** vue-router instance for runtime adapter */
  router?: VantRuntimeAdapterOptions['router'];
  /** Whether to register core adapters (default: true) */
  registerAdapters?: boolean;
}

/**
 * Create the @tnzi/mobile plugin.
 *
 * Always call the factory: `app.use(createTnziMobile({ locale: 'zh-CN' }))`.
 * The package intentionally ships no pre-built instance, because an instance
 * created at import time would freeze the locale before the app can choose one.
 */
export function createTnziMobile(options: TnziMobileOptions = {}): Plugin {
  const {
    locale = 'en-US',
    registerComponents = true,
    router,
    registerAdapters = true,
  } = options;

  return {
    install(app: App) {
      // Note: vant is a mobile UI library without built-in store management
      // If you need state management, consider using Pinia separately

      Locale.use(locale, locale === 'en-US' ? enUS : zhCN);

      if (registerComponents) {
        registerAllComponents(app);
      }

      if (registerAdapters) {
        setActiveUiAdapter(createVantUiAdapter());
        setActiveRuntimeAdapter(createVantRuntimeAdapter({ router }));
      }

      // Keep @tnzi/core's `t()` on the same locale as Vant's own strings,
      // otherwise components mix translated Vant chrome with untranslated labels.
      provideI18n(app, locale);
    },
  };
}
