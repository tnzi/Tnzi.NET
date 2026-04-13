/**
 * @tnzi/ui/plugin
 *
 * Vue 3 plugin for Tnzi UI components.
 */

import type { App } from 'vue';
import type { VueRouterLike } from './adapters/create-runtime-adapter';
import { DEFAULT_LOCALE } from '@tnzi/core/adapters/i18n';
import type { Locale } from '@tnzi/core/adapters/i18n';
import {
  getActiveStoreRuntime,
  setActiveStoreRuntime,
  setActiveUiAdapter,
  setActiveRuntimeAdapter,
  setNotificationAdapter,
  setLoadingBarAdapter,
  type StoreRuntime,
} from '@tnzi/core/adapters';
import { createUiAdapter } from './adapters/create-ui-adapter';
import { createRuntimeAdapter } from './adapters/create-runtime-adapter';
import { createNotificationAdapter } from './adapters/notification';
import { createLoadingBarAdapter } from './adapters/loading-bar';
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
  /** Vue Router instance for runtime adapter */
  router?: VueRouterLike;
  /** Whether to register composite UI and runtime adapters (default: true) */
  registerAdapters?: boolean;
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
    router,
    registerAdapters = true,
  } = options;

  return {
    install(app: App) {
      // Register composite adapters (UI + Runtime)
      if (registerAdapters) {
        setActiveUiAdapter(createUiAdapter());
        setActiveRuntimeAdapter(createRuntimeAdapter({ router }));
        setNotificationAdapter(createNotificationAdapter());
        setLoadingBarAdapter(createLoadingBarAdapter());
      }

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
