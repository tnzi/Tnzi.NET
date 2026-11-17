/**
 * @tnzi/ui
 *
 * The library-neutral Tnzi UI layer: theme context and tokens, the login
 * stack, overlay/focus primitives and the components that render without a
 * component library.
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

// Stores are not exported here: they need Pinia, an optional peer. Import
// them from `@tnzi/ui/stores`.

// Resolvers
export * from './resolvers/index';

// Utils (device-icon, avatar, placeholders)
export * from './utils/index';
