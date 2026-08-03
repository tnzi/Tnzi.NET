/**
 * @tnzi/ui-ai
 *
 * AI components for chat, agent visualization, workflow builder,
 * and admin panels. Built on Vue 3 + UnoCSS (presetWind4 + presetTnzi).
 */

import 'virtual:uno.css';
import './styles/index.css';

// Theme
export * from './theme/index';

// i18n engine + message catalogues
export * from './i18n/index';
export { en, zhCn } from './locales/index';

// Utilities
export * from './utils/index';

// Components (Phase 2+)
export * from './components/index';

// Composables (Phase 2+)
export * from './headless/index';

// Embed (Phase 3+)
export * from './embed/index';

// NOTE: nothing workflow-related is re-exported here - not the `TWorkflow*`
// components, not `Handle` / `Position`, not the `@vue-flow/core` types. They
// all live behind `@tnzi/ui-ai/workflow` so that importing the root barrel
// never pulls `@vue-flow/core` into the module graph:
//   import { Handle, Position, TWorkflowCanvas } from '@tnzi/ui-ai/workflow'
//
// The components were the leak that survived the 2026-07-26 pass: they came
// back in through `components/index.ts` below, which this barrel re-exports.
// A component does not have to mention `@vue-flow/core` for the dependency to
// travel with it, which is why the old `grep -c vue-flow dist/index.js` check
// read 0 while the graph was not actually clean.
