/**
 * @tnzi/shadcn/plugin
 *
 * Vue 3 plugin for Tnzi UI components.
 */

import type { App } from 'vue';
import { DEFAULT_LOCALE } from '@tnzi/core/adapters/i18n';
import type { Locale } from '@tnzi/core/adapters/i18n';
import {
  getActiveStoreRuntime,
  setActiveStoreRuntime,
  type StoreRuntime,
} from '@tnzi/core/adapters';
import { createShadcnMessageAdapter } from './adapters/message';
import { createShadcnDialogAdapter } from './adapters/dialog';
import { createPiniaRuntime, type PiniaRuntimeOptions } from './adapters/store';
import { registerAllComponents } from './components/register';

/** Plugin options */
export interface TnziUiOptions {
  /** Default locale for i18n */
  locale?: Locale;
  /** Custom store runtime instance */
  runtime?: StoreRuntime;
  /** Enable/disable component auto-registration */
  registerComponents?: boolean;
  /** Enable/disable Pinia state management */
  enablePinia?: boolean;
  /** Pinia runtime options */
  piniaRuntimeOptions?: PiniaRuntimeOptions;
}

/**
 * Create Tnzi UI plugin
 */
export function createTnziUi(options: TnziUiOptions = {}) {
  const {
    locale = DEFAULT_LOCALE,
    runtime,
    registerComponents = true,
    enablePinia = true,
    piniaRuntimeOptions = {},
  } = options;

  return {
    install(app: App) {
      // Setup Pinia state management
      if (enablePinia) {
        const piniaRuntime = createPiniaRuntime(piniaRuntimeOptions);
        piniaRuntime.install(app as { use: (plugin: unknown) => void });

        // Set as active runtime if no custom runtime provided
        if (!runtime) {
          setActiveStoreRuntime(piniaRuntime as unknown as StoreRuntime);
        }
      }

      // Use custom runtime if provided
      if (runtime) {
        setActiveStoreRuntime(runtime);
      }

      // Register components
      if (registerComponents) {
        registerAllComponents(app);
      }
    },
  };
}

/** Default plugin instance */
export default createTnziUi();
