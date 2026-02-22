/**
 * @tnzi/vant/plugin
 *
 * Vue 3 plugin for Vant-based mobile SPA applications.
 */

import type { App } from 'vue';
import { Locale } from 'vant';
import zhCN from 'vant/es/locale/lang/zh-CN';
import enUS from 'vant/es/locale/lang/en-US';
import { registerAllComponents } from './components/register';
import './styles/vant.css';

/** Plugin options */
export interface TnziVantOptions {
  /** Vant locale ('zh-CN' or 'en-US') */
  locale?: 'zh-CN' | 'en-US';
  /** Whether to register T* components globally (default: true) */
  registerComponents?: boolean;
}

export function createTnziVant(options: TnziVantOptions = {}) {
  const {
    locale = 'zh-CN',
    registerComponents = true,
  } = options;

  return {
    install(app: App) {
      // Note: vant is a mobile UI library without built-in store management
      // If you need state management, consider using Pinia separately

      Locale.use(locale, locale === 'en-US' ? enUS : zhCN);

      if (registerComponents) {
        registerAllComponents(app);
      }
    },
  };
}

export default createTnziVant();
