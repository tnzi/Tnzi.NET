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

// NOTE: `Handle` / `Position` and the `@vue-flow/core` types are deliberately
// NOT re-exported here. They live in `@tnzi/ui-ai/workflow` so that importing
// the root barrel never pulls `@vue-flow/core` into the module graph. Anything
// that needs them imports from the subpath:
//   import { Handle, Position, TWorkflowCanvas } from '@tnzi/ui-ai/workflow'
