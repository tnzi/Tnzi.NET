/**
 * @tnzi/ui
 *
 * Tnzi UI components based on shadcn-vue.
 * Desktop SPA components for admin dashboards and applications.
 */

import './styles/globals.css';

// Plugin
export { createTnziUi } from './plugin';
export type { TnziUiOptions } from './plugin';

// Components (semantic T* components)
export * from './components/index';

// Composables (shadcn-specific, internal use)
export * from './composables/index';

// Adapters (shadcn-specific implementations)
export * from './adapters/index';

// Stores (Pinia-based state management)
export * from './stores/index';

// Resolvers for auto-import
export * from './resolvers/index';

