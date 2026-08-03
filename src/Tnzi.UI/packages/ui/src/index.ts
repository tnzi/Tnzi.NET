/**
 * @tnzi/ui
 *
 * Tnzi UI components based on Naive UI.
 *
 * @packageDocumentation
 */

import './styles/index.css';
import 'virtual:uno.css';

// Plugin
export { default, createTnziUi } from './plugin';
export type { TnziUiOptions } from './plugin';

// Components
export * from './components/index';

// Composables
export * from './headless/index';

// Theme
export * from './theme/index';

// Adapters
export * from './adapters/index';

// Stores
export * from './stores/index';

// Resolvers
export * from './resolvers/index';

// Utils (naive-helpers, device-icon)
export * from './utils/index';

// Naive UI locale bridge (message catalogue lives in @tnzi/core/adapters/i18n)
export { getNaiveLocale, type NaiveLocaleBundle } from './locales';
