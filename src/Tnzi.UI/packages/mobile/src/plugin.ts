/**
 * @tnzi/mobile/plugin
 *
 * Vue 3 plugin for Vant-based mobile SPA applications.
 */

import type { App, Plugin } from 'vue';
import Vant, { Locale } from 'vant';
import zhCN from 'vant/es/locale/lang/zh-CN';
import enUS from 'vant/es/locale/lang/en-US';
import { provideI18n } from '@tnzi/core/adapters/i18n';
import { setActiveUiAdapter, setActiveRuntimeAdapter } from '@tnzi/core/adapters';
import { installAppUpdate, type AppUpdateOptions, type AppUpdateRouter } from '@tnzi/core/app-update';
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
  /**
   * Whether to register components globally (default: true). This covers the
   * T* components and, because their templates resolve `<van-*>` tags
   * globally, Vant itself. Switch it off to register selectively; Vant is
   * then yours to install as well.
   */
  registerComponents?: boolean;
  /** vue-router instance for runtime adapter */
  router?: VantRuntimeAdapterOptions['router'];
  /** Whether to register core adapters (default: true) */
  registerAdapters?: boolean;
  /**
   * Recovery for pages left open across a deployment. On by default: a lazy
   * route whose chunk the deployment removed loads the target in full, and a
   * new build is picked up on the next route navigation. Mobile webviews hold
   * on to a page far longer than desktop tabs, so this matters more here.
   * Needs `router` for both behaviours to reach routing; without it only
   * non-route lazy chunks are recovered. `false` turns it off. Inert on the
   * Vite dev server. See `@tnzi/core/app-update`.
   */
  appUpdate?: false | Omit<AppUpdateOptions, 'router'>;
}

/** The `router` option is typed loosely; only a real vue-router can drive navigation recovery. */
function asAppUpdateRouter(router: unknown): AppUpdateRouter | undefined {
  const candidate = router as Partial<AppUpdateRouter> | undefined;
  return candidate &&
    typeof candidate.beforeEach === 'function' &&
    typeof candidate.onError === 'function' &&
    typeof candidate.resolve === 'function'
    ? (candidate as AppUpdateRouter)
    : undefined;
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
    appUpdate,
  } = options;

  return {
    install(app: App) {
      // Note: vant is a mobile UI library without built-in store management
      // If you need state management, consider using Pinia separately

      Locale.use(locale, locale === 'en-US' ? enUS : zhCN);

      if (registerComponents) {
        // The T* templates use <van-field> and friends as global tags, so a T*
        // component without Vant registered renders as empty custom elements:
        // no <form>, no <input>, a submit that never fires - and only a dev-mode
        // warning to say so. Skipped when the consumer already installed Vant,
        // which would otherwise draw Vue's "already applied" warning.
        if (!app.component('VanField')) {
          app.use(Vant);
        }
        registerAllComponents(app);
      }

      if (registerAdapters) {
        setActiveUiAdapter(createVantUiAdapter());
        setActiveRuntimeAdapter(createVantRuntimeAdapter({ router }));
      }

      // Keep @tnzi/core's `t()` on the same locale as Vant's own strings,
      // otherwise components mix translated Vant chrome with untranslated labels.
      provideI18n(app, locale);

      if (appUpdate !== false) {
        installAppUpdate({ ...appUpdate, router: asAppUpdateRouter(router) });
      }
    },
  };
}
