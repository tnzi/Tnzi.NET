/**
 * @tnzi/ui-ai
 *
 * AI components for chat, agent visualization, workflow builder,
 * and admin panels. Built on Vue 3 + UnoCSS (presetWind4 + presetTnzi).
 */

import 'virtual:uno.css';
import './styles/index.css';

// Theme
export * from './themes/index';

// Locale
export * from './locale/index';

// Utilities
export { formatCompactNumber } from './lib/utils';

// Components (Phase 2+)
export * from './components/index';

// Composables (Phase 2+)
export * from './composables/index';

// Chat (Phase 2+)
export * from './chat/index';

// Embed (Phase 3+)
export * from './embed/index';

// Vue Flow primitives — explicit const re-bind (not `export *`) because
// Vite's `preserveModules: true` build aggressively tree-shakes transitive
// `export {} from 'pkg'` chains that aren't referenced internally, even
// though they are part of the public API. Binding them to local consts
// forces them into the bundle so consumers writing custom node templates
// against `WorkflowCanvas` can import `Handle`/`Position` without taking a
// direct `@vue-flow/core` dependency.
import { Handle as _VfHandle, Position as _VfPosition } from '@vue-flow/core';
export const Handle = _VfHandle;
export const Position = _VfPosition;
export type { NodeProps, EdgeProps, Connection, NodeChange, EdgeChange } from '@vue-flow/core';
