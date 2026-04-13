/**
 * @tnzi/mobile/plugin
 *
 * Vue 3 plugin for Vant-based mobile SPA applications.
 */

import type { App } from 'vue';
import { Locale } from 'vant';
import zhCN from 'vant/es/locale/lang/zh-CN';
import enUS from 'vant/es/locale/lang/en-US';
import { setActiveUiAdapter, setActiveRuntimeAdapter } from '@tnzi/core/adapters';
import { registerAllComponents } from './components/register';
import { createVantUiAdapter } from './adapters/create-ui-adapter';
import { createVantRuntimeAdapter } from './adapters/create-runtime-adapter';
import type { VantRuntimeAdapterOptions } from './adapters/create-runtime-adapter';
import './styles/vant.css';

/** Plugin options */
export interface TnziMobileOptions {
  /** Vant locale ('zh-CN' or 'en-US') */
  locale?: 'zh-CN' | 'en-US';
  /** Whether to register components globally (default: true) */
  registerComponents?: boolean;
  /** vue-router instance for runtime adapter */
  router?: VantRuntimeAdapterOptions['router'];
  /** Whether to register core adapters (default: true) */
  registerAdapters?: boolean;
}

export function createTnziMobile(options: TnziMobileOptions = {}) {
  const {
    locale = 'zh-CN',
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
    },
  };
}

export default createTnziMobile();
